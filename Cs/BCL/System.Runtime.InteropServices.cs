namespace System.Runtime.InteropServices
{
    public static class Marshal
    {
        public static IntPtr AllocHGlobal(int cb) => AllocHGlobal((nint)cb);
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static unsafe IntPtr AllocHGlobal(nint cb) => IntPtr.Zero;
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static unsafe void FreeHGlobal(IntPtr hglobal) { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static unsafe void LinuxRequestShutdown() { throw new PlatformNotSupportedException(); }
    }
    public static unsafe class MemoryMarshal
    {
        [Intrinsic]
        public static ref T GetArrayDataReference<T>(T[] array)
        {
            throw new NotSupportedException();
        }

        [Intrinsic]
        public static unsafe ref byte GetArrayDataReference(Array array)
        {
            //return ref Unsafe.AddByteOffset(ref Unsafe.As<RawData>(array).Data, 
            //    (nuint)RuntimeHelpers.GetMethodTable(array)->BaseSize - (nuint)(2 * sizeof(IntPtr)));
            return ref *(byte*)0;
        }

        public static ref T GetReference<T>(Span<T> span) => ref span._reference;
        public static ref T GetReference<T>(ReadOnlySpan<T> span) => ref span._reference;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Write<T>(Span<byte> destination, in T value)
            where T : struct
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                throw new NotSupportedException();
            }
            if ((uint)sizeof(T) > (uint)destination.Length)
            {
                throw new ArgumentOutOfRangeException();
            }
            Unsafe.WriteUnaligned<T>(ref GetReference<byte>(destination), value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool TryWrite<T>(Span<byte> destination, in T value)
            where T : struct
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                throw new NotSupportedException();
            }
            if (sizeof(T) > (uint)destination.Length)
            {
                return false;
            }
            Unsafe.WriteUnaligned<T>(ref GetReference<byte>(destination), value);
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe T Read<T>(ReadOnlySpan<byte> source)
            where T : struct
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                throw new NotSupportedException();
            }
            if (sizeof(T) > source.Length)
            {
                throw new ArgumentOutOfRangeException();
            }
            return Unsafe.ReadUnaligned<T>(ref GetReference<byte>(source));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool TryRead<T>(ReadOnlySpan<byte> source, out T value)
            where T : struct
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                throw new NotSupportedException();
            }
            if (sizeof(T) > (uint)source.Length)
            {
                value = default;
                return false;
            }
            value = Unsafe.ReadUnaligned<T>(ref GetReference<byte>(source));
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Span<byte> AsBytes<T>(Span<T> span)
            where T : struct
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                throw new NotSupportedException();

            return new Span<byte>(
                ref Unsafe.As<T, byte>(ref GetReference(span)),
                checked(span.Length * sizeof(T)));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ReadOnlySpan<byte> AsBytes<T>(ReadOnlySpan<T> span)
            where T : struct
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                throw new NotSupportedException();

            return new ReadOnlySpan<byte>(
                ref Unsafe.As<T, byte>(ref GetReference(span)),
                checked(span.Length * sizeof(T)));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Span<TTo> Cast<TFrom, TTo>(Span<TFrom> span)
            where TFrom : struct
            where TTo : struct
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<TFrom>())
                throw new InvalidCastException();
            if (RuntimeHelpers.IsReferenceOrContainsReferences<TTo>())
                throw new InvalidCastException();

            // Use unsigned integers - unsigned division by constant (especially by power of 2)
            // and checked casts are faster and smaller.
            uint fromSize = (uint)sizeof(TFrom);
            uint toSize = (uint)sizeof(TTo);
            uint fromLength = (uint)span.Length;
            int toLength;
            if (fromSize == toSize)
            {
                // Special case for same size types - `(ulong)fromLength * (ulong)fromSize / (ulong)toSize`
                // should be optimized to just `length` but the JIT doesn't do that today.
                toLength = (int)fromLength;
            }
            else if (fromSize == 1)
            {
                // Special case for byte sized TFrom - `(ulong)fromLength * (ulong)fromSize / (ulong)toSize`
                // becomes `(ulong)fromLength / (ulong)toSize` but the JIT can't narrow it down to `int`
                // and can't eliminate the checked cast. This also avoids a 32 bit specific issue,
                // the JIT can't eliminate long multiply by 1.
                toLength = (int)(fromLength / toSize);
            }
            else
            {
                // Ensure that casts are done in such a way that the JIT is able to "see"
                // the uint->ulong casts and the multiply together so that on 32 bit targets
                // 32x32to64 multiplication is used.
                ulong toLengthUInt64 = (ulong)fromLength * (ulong)fromSize / (ulong)toSize;
                toLength = checked((int)toLengthUInt64);
            }

            return new Span<TTo>(
                ref Unsafe.As<TFrom, TTo>(ref span._reference),
                toLength);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ReadOnlySpan<TTo> Cast<TFrom, TTo>(ReadOnlySpan<TFrom> span)
            where TFrom : struct
            where TTo : struct
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<TFrom>())
                throw new InvalidCastException();
            if (RuntimeHelpers.IsReferenceOrContainsReferences<TTo>())
                throw new InvalidCastException();

            // Use unsigned integers - unsigned division by constant (especially by power of 2)
            // and checked casts are faster and smaller.
            uint fromSize = (uint)sizeof(TFrom);
            uint toSize = (uint)sizeof(TTo);
            uint fromLength = (uint)span.Length;
            int toLength;
            if (fromSize == toSize)
            {
                // Special case for same size types - `(ulong)fromLength * (ulong)fromSize / (ulong)toSize`
                // should be optimized to just `length` but the JIT doesn't do that today.
                toLength = (int)fromLength;
            }
            else if (fromSize == 1)
            {
                // Special case for byte sized TFrom - `(ulong)fromLength * (ulong)fromSize / (ulong)toSize`
                // becomes `(ulong)fromLength / (ulong)toSize` but the JIT can't narrow it down to `int`
                // and can't eliminate the checked cast. This also avoids a 32 bit specific issue,
                // the JIT can't eliminate long multiply by 1.
                toLength = (int)(fromLength / toSize);
            }
            else
            {
                // Ensure that casts are done in such a way that the JIT is able to "see"
                // the uint->ulong casts and the multiply together so that on 32 bit targets
                // 32x32to64 multiplication is used.
                ulong toLengthUInt64 = (ulong)fromLength * (ulong)fromSize / (ulong)toSize;
                toLength = checked((int)toLengthUInt64);
            }

            return new ReadOnlySpan<TTo>(
                ref Unsafe.As<TFrom, TTo>(ref GetReference(span)),
                toLength);
        }
    }
    public static class CollectionsMarshal
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<T> AsSpan<T>(List<T>? list)
        {
            Span<T> span = default;
            if (list is not null)
            {
                int size = list._size;
                T[] items = list._items;

                if ((uint)size > (uint)items.Length)
                {
                    // List<T> was erroneously mutated concurrently with this call, leading to a count larger than its array.
                    throw new InvalidOperationException();
                }

                span = new Span<T>(ref MemoryMarshal.GetArrayDataReference<T>(items), size);
            }

            return span;
        }

        public static void SetCount<T>(List<T> list, int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            list._version++;

            if (count > list.Capacity)
            {
                list.Grow(count);
            }
            else if (count < list._size && RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                Array.Clear(list._items, count, list._size - count);
            }

            list._size = count;
        }
    }
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class UnmanagedCallersOnlyAttribute : Attribute
    {
        public UnmanagedCallersOnlyAttribute()
        {
        }

        public string? EntryPoint;
    }
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class DllImportAttribute : Attribute
    {
        public DllImportAttribute(string dllName)
        {
            Value = dllName;
        }

        public string Value { get; }

        public string? EntryPoint;
        public CharSet CharSet = CharSet.None;
        public bool SetLastError;
        public bool ExactSpelling;
        public CallingConvention CallingConvention = CallingConvention.Winapi;
        public bool BestFitMapping;
        public bool PreserveSig = true;
        public bool ThrowOnUnmappableChar;
    }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public sealed class StructLayoutAttribute : Attribute
    {
        public StructLayoutAttribute(LayoutKind layoutKind)
        {
            Value = layoutKind;
        }

        public StructLayoutAttribute(short layoutKind)
        {
            Value = (LayoutKind)layoutKind;
        }

        public LayoutKind Value { get; }

        public int Pack;
        public int Size;
        public CharSet CharSet;
    }
    public enum CallingConvention
    {
        Winapi = 1,
        Cdecl = 2,
        StdCall = 3,
        ThisCall = 4,
        FastCall = 5,
    }
    public enum CharSet
    {
        None = 1,        // User didn't specify how to marshal strings.
        Ansi = 2,        // Strings should be marshalled as ANSI 1 byte chars.
        Unicode = 3,     // Strings should be marshalled as Unicode 2 byte chars.
        Auto = 4,        // Marshal Strings in the right way for the target system.
    }
    public enum LayoutKind
    {
        Sequential = 0,
        Explicit = 2,
        Auto = 3,
    }
}