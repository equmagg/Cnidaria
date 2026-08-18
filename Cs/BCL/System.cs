using System.Globalization;
using System.Runtime.CompilerServices;

namespace System
{
    public static class Environment
    {
        internal const bool IsDebug = false;
        internal const bool IsRelease = !IsDebug;
        public static bool Is64BitProcess => IntPtr.Size == 8;
        internal const string NewLineConst = "\n";
        public static string NewLine => NewLineConst;
        private static volatile OperatingSystem? s_osVersion;
        public static OperatingSystem OSVersion
        {
            get
            {
                OperatingSystem? osVersion = s_osVersion;
                if (osVersion == null)
                {
                    System.Threading.Interlocked.CompareExchange(ref s_osVersion, GetOSVersion(), null);
                    osVersion = s_osVersion;
                }
                return osVersion;
            }
        }

        private static OperatingSystem GetOSVersion()
        {
            throw new NotSupportedException();
        }

        public static int ProcessorCount { get; } = 1;
        /// <summary>Gets the number of milliseconds elapsed since the system started.</summary>
        /// <value>A 32-bit signed integer containing the amount of time in milliseconds that has passed since the last time the computer was started.</value>
        public static int TickCount => (int)TickCount64;
        /// <summary>Gets the number of milliseconds elapsed since the system started.</summary>
        public static long TickCount64 => throw new NotSupportedException();
    }

    public struct Void { }

    public class Object
    {
        public Object() { }
        public virtual string ToString() { return "System.Object"; }
        public virtual bool Equals(object? obj)
        {
            return this == obj;
        }
        public static bool Equals(object? objA, object? objB)
        {
            if (objA == objB)
            {
                return true;
            }
            if (objA == null || objB == null)
            {
                return false;
            }
            return objA.Equals(objB);
        }
        public static bool ReferenceEquals(object? objA, object? objB)
        {
            return objA == objB;
        }
        public virtual int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        // Returns a Type object which represent this object instance.
        [Intrinsic]
        public unsafe Type GetType()
        {
            //MethodTable* pMT = RuntimeHelpers.GetMethodTable(this);
            //RuntimeType type = RuntimeTypeHandle.GetRuntimeType(pMT);
            //GC.KeepAlive(this);
            //return type;
            return null!;
        }
    }

    public class ValueType { }
    public abstract class Enum : ValueType
    {
        protected Enum() { }
        public bool HasFlag(Enum flag)
        {
            return false;
        }
    }
    public class Array
    {
        private static class EmptyArray<T>
        {
            internal static readonly T[] Value = new T[0];
        }
        public static int MaxLength => 0X7FFFFFC7;
        public int Length
        {
            [MethodImpl(MethodImplOptions.InternalCall)]
            get { return 0; }
        }
        public System.Collections.IEnumerator GetEnumerator()
        {
            return null;
        }

        public static T[] Empty<T>()
        {
            return EmptyArray<T>.Value;
        }

        public static int IndexOf<T>(T[] array, T value)
        {
            if ((object)array == null) throw new ArgumentNullException("array");
            return IndexOf<T>(array, value, 0, array.Length);
        }

        public static int IndexOf<T>(T[] array, T value, int startIndex)
        {
            if ((object)array == null) throw new ArgumentNullException("array");

            int len = array.Length;
            if ((uint)startIndex > (uint)len) throw new ArgumentOutOfRangeException("startIndex");

            return IndexOf<T>(array, value, startIndex, len - startIndex);
        }

        public static int IndexOf<T>(T[] array, T value, int startIndex, int count)
        {
            if ((object)array == null) throw new ArgumentNullException("array");

            int len = array.Length;
            if ((uint)startIndex > (uint)len) throw new ArgumentOutOfRangeException("startIndex");
            if (count < 0 || startIndex > len - count) throw new ArgumentOutOfRangeException("count");

            int end = startIndex + count;

            if ((object)value == null)
            {
                for (int i = startIndex; i < end; i++)
                {
                    if ((object)array[i] == null)
                        return i;
                }

                return -1;
            }

            object boxedValue = value;
            for (int i = startIndex; i < end; i++)
            {
                if (boxedValue.Equals(array[i]))
                    return i;
            }

            return -1;
        }

        public static void Clear(Array array)
        {
            if ((object)array == null) throw new ArgumentNullException("array");
            Clear(array, 0, array.Length);
        }

        public static void Clear(Array array, int index, int length)
        {
            if ((object)array == null) throw new ArgumentNullException("array");
            if (index < 0) throw new ArgumentOutOfRangeException("index");
            if (length < 0) throw new ArgumentOutOfRangeException("length");

            int alen = array.Length;
            if (alen - index < length) throw new IndexOutOfRangeException();
            if (length == 0) return;

            ClearInternal(array, index, length);
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static void ClearInternal(Array array, int index, int length)
        {
            // handled in runtime
        }

        public static void Fill<T>(T[] array, T value)
        {
            if ((object)array == null) throw new ArgumentNullException("array");
            Fill<T>(array, value, 0, array.Length);
        }
        public static void Fill<T>(T[] array, T value, int startIndex, int count)
        {
            if ((object)array == null) throw new ArgumentNullException("array");

            int len = array.Length;
            if ((uint)startIndex > (uint)len) throw new ArgumentOutOfRangeException("startIndex");
            if (count < 0 || startIndex > len - count) throw new ArgumentOutOfRangeException("count");
            if (count == 0) return;

            ref T r0 = ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference<T>(array);
            ref T dst = ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref r0, startIndex);

            for (int i = 0; i < count; i++)
                System.Runtime.CompilerServices.Unsafe.Add<T>(ref dst, i) = value;
        }
        public static void Resize<T>([NotNull] ref T[] array, int newSize)
        {
            if (newSize < 0)
                throw new ArgumentOutOfRangeException();

            T[] larray = array; // local copy
            if (larray == null)
            {
                array = new T[newSize];
                return;
            }

            if (larray.Length != newSize)
            {
                T[] newArray = new T[newSize];
                Buffer.Memmove<T>(
                    ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference<T>(newArray),
                    ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference<T>(larray),
                    (uint)Math.Min(newSize, larray.Length));
                array = newArray;
            }
        }

        public static void Copy(Array sourceArray, Array destinationArray, long length)
        {
            int ilength = (int)length;
            if (length != ilength)
                throw new ArgumentOutOfRangeException("length");

            Copy(sourceArray, destinationArray, ilength);
        }
        public static void Copy(Array sourceArray, long sourceIndex, Array destinationArray, long destinationIndex, long length)
        {
            int isourceIndex = (int)sourceIndex;
            int idestinationIndex = (int)destinationIndex;
            int ilength = (int)length;

            if (sourceIndex != isourceIndex)
                throw new ArgumentOutOfRangeException("sourceIndex");
            if (destinationIndex != idestinationIndex)
                throw new ArgumentOutOfRangeException("destinationIndex");
            if (length != ilength)
                throw new ArgumentOutOfRangeException("length");

            Copy(sourceArray, isourceIndex, destinationArray, idestinationIndex, ilength);
        }
        public static unsafe void Copy(Array sourceArray, Array destinationArray, int length)
        {
            if ((object)sourceArray == null) throw new ArgumentNullException("sourceArray");
            if ((object)destinationArray == null) throw new ArgumentNullException("destinationArray");
            if (length < 0) throw new ArgumentOutOfRangeException("length");

            Copy(sourceArray, 0, destinationArray, 0, length);
        }
        public static unsafe void Copy(Array sourceArray, int sourceIndex, Array destinationArray, int destinationIndex, int length)
        {
            if ((object)sourceArray == null) throw new ArgumentNullException("sourceArray");
            if ((object)destinationArray == null) throw new ArgumentNullException("destinationArray");

            if (sourceIndex < 0) throw new ArgumentOutOfRangeException("sourceIndex");
            if (destinationIndex < 0) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length < 0) throw new ArgumentOutOfRangeException("length");

            int srcLen = sourceArray.Length;
            int dstLen = destinationArray.Length;

            if ((uint)sourceIndex > (uint)srcLen) throw new ArgumentOutOfRangeException("sourceIndex");
            if ((uint)destinationIndex > (uint)dstLen) throw new ArgumentOutOfRangeException("destinationIndex");

            if (srcLen - sourceIndex < length) throw new ArgumentException();
            if (dstLen - destinationIndex < length) throw new ArgumentException();

            if (length == 0) return;

            if (!CopyInternal(sourceArray, sourceIndex, destinationArray, destinationIndex, length))
                throw new ArrayTypeMismatchException();
        }
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static bool CopyInternal(Array sourceArray, int sourceIndex, Array destinationArray, int destinationIndex, int length)
            => false;
    }

    public abstract unsafe class Delegate
    {
        internal object? _target; // do not rename
        internal nint _methodPtr; // do not rename
        internal nint _methodModule;

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static Delegate? Combine(Delegate? a, Delegate? b) => null;

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static Delegate? Remove(Delegate? source, Delegate? value) => null;
    }
    public abstract class MulticastDelegate : Delegate
    {
        private object? _invocationList;
        private nint _invocationCount;
    }

    public enum StringComparison
    {
        CurrentCulture = 0,
        CurrentCultureIgnoreCase = 1,
        InvariantCulture = 2,
        InvariantCultureIgnoreCase = 3,
        Ordinal = 4,
        OrdinalIgnoreCase = 5,
    }
    [Flags]
    public enum StringSplitOptions
    {
        None = 0,
        RemoveEmptyEntries = 1,
        TrimEntries = 2
    }
    public sealed class String
    {
        private readonly int _stringLength;  // do not rename
        private char _firstChar;  // do not rename

        /// <summary>Maximum length allowed for a string.</summary>
        /// <remarks>Keep in sync with AllocateString in gchelpers.cpp.</remarks>
        internal const int MaxLength = 0x3FFFFFDF;
        public const string Empty = "";
        public int Length
        {
            [Intrinsic]
            get { return _stringLength; }
        }
        [NonVersionable]
        public ref readonly char GetPinnableReference() { return ref _firstChar; }
        internal ref char GetRawStringData() { return ref _firstChar; }
        internal ref byte GetRawStringDataAsUInt8() { return ref System.Runtime.CompilerServices.Unsafe.As<char, byte>(ref _firstChar); }
        internal ref ushort GetRawStringDataAsUInt16() { return ref System.Runtime.CompilerServices.Unsafe.As<char, ushort>(ref _firstChar); }
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static string FastAllocateString(int length) { return null; }
        internal String() { }
        public String(Char ch, Int32 Length) { }
        public String(char ch, int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException("length");
            ref char dst = ref GetRawStringData();
            for (int i = 0; i < length; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = ch;
        }
        public String(char[] value)
        {
            if (value == null) throw new ArgumentNullException("length");
            int n = value.Length;
            ref char dst = ref GetRawStringData();
            for (int i = 0; i < n; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = value[i];
        }
        public String(char[] value, int startIndex, int length)
        {
            if (value == null) throw new ArgumentNullException("length");
            if ((uint)startIndex > (uint)value.Length) throw new ArgumentOutOfRangeException("startIndex");
            if (length < 0) throw new ArgumentOutOfRangeException("length");
            if (startIndex + length > value.Length) throw new ArgumentOutOfRangeException("length");

            ref char dst = ref GetRawStringData();
            for (int i = 0; i < length; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = value[startIndex + i];
        }
        public String(ReadOnlySpan<char> value)
        {
            ref char dst = ref GetRawStringData();
            for (int i = 0; i < value.Length; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = value[i];
        }
        public unsafe String(char* value)
        {
            if (value == null) throw new ArgumentNullException("value");

            ref char dst = ref GetRawStringData();
            for (int i = 0; i < Length; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = value[i];
        }

        public char this[int index]
        {
            [Intrinsic]
            get
            {
                if ((uint)index >= (uint)_stringLength)
                    throw new IndexOutOfRangeException();
                return System.Runtime.CompilerServices.Unsafe.Add<char>(ref _firstChar, index);
            }
        }

        /// <summary>Copies the contents of this string into the destination span.</summary>
        /// <param name="destination">The span into which to copy this string's contents.</param>
        /// <exception cref="ArgumentException">The destination span is shorter than the source string.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Span<char> destination)
        {
            if ((uint)Length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref _firstChar, (uint)Length);
            }
            else
            {
                throw new ArgumentException();
            }
        }

        /// <summary>Copies the contents of this string into the destination span.</summary>
        /// <param name="destination">The span into which to copy this string's contents.</param>
        /// <returns>true if the data was copied; false if the destination was too short to fit the contents of the string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCopyTo(Span<char> destination)
        {
            bool retVal = false;
            if ((uint)Length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref _firstChar, (uint)Length);
                retVal = true;
            }
            return retVal;
        }

        public override string ToString() => this;
        // Determines whether two strings match.
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (ReferenceEquals(this, obj))
                return true;

            if (obj is not string str)
                return false;

            if (this.Length != str.Length)
                return false;

            return EqualsHelper(this, str);
        }
        public bool Equals([NotNullWhen(true)] string? value)
        {
            if (ReferenceEquals(this, value))
                return true;
            if (value == null)
                return false;
            int n = Length;
            if (n != value.Length) return false;
            ref char a = ref GetRawStringData();
            ref char b = ref value.GetRawStringData();
            for (int i = 0; i < n; i++)
            {
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, i) !=
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, i))
                    return false;
            }
            return true;
        }
        public bool Equals([NotNullWhen(true)] string? value, StringComparison comparisonType)
        {
            if (ReferenceEquals(this, value))
            {
                CheckStringComparison(comparisonType);
                return true;
            }

            if (value is null)
            {
                CheckStringComparison(comparisonType);
                return false;
            }

            switch (comparisonType)
            {
                case StringComparison.CurrentCulture:
                case StringComparison.CurrentCultureIgnoreCase:
                    return System.Globalization.CultureInfo.CurrentCulture.CompareInfo.Compare(this, value, GetCaseCompareOfComparisonCulture(comparisonType)) == 0;

                case StringComparison.InvariantCulture:
                case StringComparison.InvariantCultureIgnoreCase:
                    return System.Globalization.CompareInfo.Invariant.Compare(this, value, GetCaseCompareOfComparisonCulture(comparisonType)) == 0;

                case StringComparison.Ordinal:
                    if (this.Length != value.Length)
                        return false;
                    return EqualsHelper(this, value);

                case StringComparison.OrdinalIgnoreCase:
                    if (this.Length != value.Length)
                        return false;

                    return EqualsOrdinalIgnoreCaseNoLengthCheck(this, value);

                default:
                    throw new ArgumentException();
            }
        }
        public static bool Equals(string? a, string? b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a is null || b is null || a.Length != b.Length)
            {
                return false;
            }

            return EqualsHelper(a, b);
        }
        public static bool Equals(string? a, string? b, StringComparison comparisonType)
        {
            if (ReferenceEquals(a, b))
            {
                CheckStringComparison(comparisonType);
                return true;
            }

            if (a is null || b is null)
            {
                CheckStringComparison(comparisonType);
                return false;
            }

            switch (comparisonType)
            {
                case StringComparison.CurrentCulture:
                case StringComparison.CurrentCultureIgnoreCase:
                    return CultureInfo.CurrentCulture.CompareInfo.Compare(a, b, GetCaseCompareOfComparisonCulture(comparisonType)) == 0;

                case StringComparison.InvariantCulture:
                case StringComparison.InvariantCultureIgnoreCase:
                    return CompareInfo.Invariant.Compare(a, b, GetCaseCompareOfComparisonCulture(comparisonType)) == 0;

                case StringComparison.Ordinal:
                    if (a.Length != b.Length)
                        return false;
                    return EqualsHelper(a, b);

                case StringComparison.OrdinalIgnoreCase:
                    if (a.Length != b.Length)
                        return false;

                    return EqualsOrdinalIgnoreCaseNoLengthCheck(a, b);

                default:
                    throw new ArgumentException();
            }
        }
        private static bool EqualsOrdinalIgnoreCaseNoLengthCheck(string strA, string strB)
        {
            return System.Globalization.Ordinal.EqualsIgnoreCase(ref strA.GetRawStringData(), ref strB.GetRawStringData(), strB.Length);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool EqualsHelper(string strA, string strB)
        {
            return SpanHelpers.SequenceEqual(
                ref strA.GetRawStringDataAsUInt8(),
                ref strB.GetRawStringDataAsUInt8(),
                ((uint)strA.Length) * sizeof(char));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareOrdinalHelper(string strA, int indexA, int countA, string strB, int indexB, int countB)
        {
            return SpanHelpers.SequenceCompareTo(
                ref Unsafe.Add(ref strA.GetRawStringData(), (nint)(uint)indexA /* force zero-extension */), countA,
                ref Unsafe.Add(ref strB.GetRawStringData(), (nint)(uint)indexB /* force zero-extension */), countB);
        }
        internal static void CheckStringComparison(StringComparison comparisonType)
        {
            // Single comparison to check if comparisonType is within [CurrentCulture .. OrdinalIgnoreCase]
            if ((uint)comparisonType > (uint)StringComparison.OrdinalIgnoreCase)
            {
                throw new ArgumentException();
            }
        }
        internal static System.Globalization.CompareOptions GetCaseCompareOfComparisonCulture(StringComparison comparisonType)
        {
            // Culture enums can be & with CompareOptions.IgnoreCase 0x01 to extract if IgnoreCase or CompareOptions.None 0x00
            //
            // CompareOptions.None                          0x00
            // CompareOptions.IgnoreCase                    0x01
            //
            // StringComparison.CurrentCulture:             0x00
            // StringComparison.InvariantCulture:           0x02
            // StringComparison.Ordinal                     0x04
            //
            // StringComparison.CurrentCultureIgnoreCase:   0x01
            // StringComparison.InvariantCultureIgnoreCase: 0x03
            // StringComparison.OrdinalIgnoreCase           0x05

            return (System.Globalization.CompareOptions)((int)comparisonType & (int)System.Globalization.CompareOptions.IgnoreCase);
        }
        private static System.Globalization.CompareOptions GetCompareOptionsFromOrdinalStringComparison(StringComparison comparisonType)
        {
            // StringComparison.Ordinal (0x04) --> CompareOptions.Ordinal (0x4000_0000)
            // StringComparison.OrdinalIgnoreCase (0x05) -> CompareOptions.OrdinalIgnoreCase (0x1000_0000)

            int ct = (int)comparisonType;
            return (System.Globalization.CompareOptions)((ct & -ct) << 28); // neg and shl
        }
        public static bool operator ==(string left, string right)
        {
            if ((object)left == (object)right) return true;
            if ((object)left == null || (object)right == null) return false;
            return left.Equals(right);
        }
        public static bool operator !=(string left, string right) => !(left == right);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            ulong seed = Marvin.DefaultSeed;

            // Multiplication below will not overflow since going from positive Int32 to UInt32.
            return Marvin.ComputeHash32(ref Unsafe.As<char, byte>(ref _firstChar), (uint)_stringLength * 2 /* in bytes, not chars */, (uint)seed, (uint)(seed >> 32));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetHashCodeOrdinalIgnoreCase()
        {
            ulong seed = Marvin.DefaultSeed;
            return Marvin.ComputeHash32OrdinalIgnoreCase(ref _firstChar, _stringLength /* in chars, not bytes */, (uint)seed, (uint)(seed >> 32));
        }
        // A span-based equivalent of String.GetHashCode(). Computes an ordinal hash code.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetHashCode(ReadOnlySpan<char> value)
        {
            ulong seed = Marvin.DefaultSeed;

            // Multiplication below will not overflow since going from positive Int32 to UInt32.
            return Marvin.ComputeHash32(ref Unsafe.As<char, byte>(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value)), (uint)value.Length * 2 /* in bytes, not chars */, (uint)seed, (uint)(seed >> 32));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetHashCodeOrdinalIgnoreCase(ReadOnlySpan<char> value)
        {
            ulong seed = Marvin.DefaultSeed;
            return Marvin.ComputeHash32OrdinalIgnoreCase(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value), value.Length /* in chars, not bytes */, (uint)seed, (uint)(seed >> 32));
        }
        // Important GetNonRandomizedHashCode{OrdinalIgnoreCase} notes:
        //
        // Use if and only if 'Denial of Service' attacks are not a concern (i.e. never used for free-form user input),
        // or are otherwise mitigated.
        //
        // The string-based implementation relies on System.String being null terminated. All reads are performed
        // two characters at a time, so for odd-length strings, the final read will include the null terminator.
        // This implementation must not be used as-is with spans, or otherwise arbitrary char refs/pointers, as
        // they're not guaranteed to be null-terminated.
        //
        // For spans, we must produce the exact same value as is used for strings: consumers like Dictionary<>
        // rely on str.GetNonRandomizedHashCode() == GetNonRandomizedHashCode(str.AsSpan()). As such, we must
        // restructure the comparison so that for odd-length spans, we simulate the null terminator and include
        // it in the hash computation exactly as does str.GetNonRandomizedHashCode().
        internal unsafe int GetNonRandomizedHashCode()
        {
            fixed (char* src = &_firstChar)
            {
                uint hash1 = (5381 << 16) + 5381;
                uint hash2 = hash1;

                uint* ptr = (uint*)src;
                int length = Length;

                while (length > 2)
                {
                    length -= 4;
                    hash1 = (System.Numerics.BitOperations.RotateLeft(hash1, 5) + hash1) ^ ptr[0];
                    hash2 = (System.Numerics.BitOperations.RotateLeft(hash2, 5) + hash2) ^ ptr[1];
                    ptr += 2;
                }

                if (length > 0)
                {
                    hash2 = (System.Numerics.BitOperations.RotateLeft(hash2, 5) + hash2) ^ ptr[0];
                }

                return (int)(hash1 + (hash2 * 1566083941));
            }
        }

        internal static unsafe int GetNonRandomizedHashCode(ReadOnlySpan<char> span)
        {
            uint hash1 = (5381 << 16) + 5381;
            uint hash2 = hash1;

            int length = span.Length;
            fixed (char* src = &System.Runtime.InteropServices.MemoryMarshal.GetReference(span))
            {
                uint* ptr = (uint*)src;

            LengthSwitch:
                switch (length)
                {
                    default:
                        do
                        {
                            length -= 4;
                            hash1 = System.Numerics.BitOperations.RotateLeft(hash1, 5) + hash1 ^ Unsafe.ReadUnaligned<uint>(ptr);
                            hash2 = System.Numerics.BitOperations.RotateLeft(hash2, 5) + hash2 ^ Unsafe.ReadUnaligned<uint>(ptr + 1);
                            ptr += 2;
                        }
                        while (length >= 4);
                        goto LengthSwitch;

                    case 3:
                        hash1 = BitOperations.RotateLeft(hash1, 5) + hash1 ^ Unsafe.ReadUnaligned<uint>(ptr);
                        uint p1 = *(char*)(ptr + 1);
                        if (!BitConverter.IsLittleEndian)
                        {
                            p1 <<= 16;
                        }

                        hash2 = BitOperations.RotateLeft(hash2, 5) + hash2 ^ p1;
                        break;

                    case 2:
                        hash2 = BitOperations.RotateLeft(hash2, 5) + hash2 ^ Unsafe.ReadUnaligned<uint>(ptr);
                        break;

                    case 1:
                        uint p0 = *(char*)ptr;
                        if (!BitConverter.IsLittleEndian)
                        {
                            p0 <<= 16;
                        }

                        hash2 = BitOperations.RotateLeft(hash2, 5) + hash2 ^ p0;
                        break;

                    case 0:
                        break;
                }
            }

            return (int)(hash1 + (hash2 * 1_566_083_941));
        }
        public static bool IsNullOrEmpty(string value) => (object)value == null || value.Length == 0;
        public static bool IsNullOrWhiteSpace(string value)
        {
            if ((object)value == null) return true;
            int n = value.Length;
            if (n == 0) return true;

            ref char p = ref value.GetRawStringData();
            for (int i = 0; i < n; i++)
            {
                if (!Char.IsWhiteSpace(System.Runtime.CompilerServices.Unsafe.Add<char>(ref p, i)))
                    return false;
            }
            return true;
        }
        public ReadOnlySpan<char> AsSpan()
        {
            ref char r = ref GetRawStringData();
            return new ReadOnlySpan<char>(ref r, Length);
        }
        public ReadOnlySpan<char> AsSpan(int start)
        {
            int len = Length;
            if ((uint)start > (uint)len)
                throw new ArgumentOutOfRangeException("start");

            ref char r = ref GetRawStringData();
            return new ReadOnlySpan<char>(
                ref System.Runtime.CompilerServices.Unsafe.Add<char>(ref r, start),
                len - start);
        }
        public ReadOnlySpan<char> AsSpan(int start, int length)
        {
            int len = Length;
            if ((uint)start > (uint)len)
                throw new ArgumentOutOfRangeException("start");
            if ((uint)length > (uint)(len - start))
                throw new ArgumentOutOfRangeException("length");

            ref char r = ref GetRawStringData();
            return new ReadOnlySpan<char>(
                ref System.Runtime.CompilerServices.Unsafe.Add<char>(ref r, start),
                length);
        }

        /// <summary>Creates a new string by using the specified provider to control the formatting of the specified interpolated string.</summary>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <param name="handler">The interpolated string.</param>
        /// <returns>The string that results for formatting the interpolated string using the specified format provider.</returns>
        public static string Create(
            IFormatProvider? provider,
            [InterpolatedStringHandlerArgument(nameof(provider))] ref DefaultInterpolatedStringHandler handler) =>
            handler.ToStringAndClear();

        /// <summary>Creates a new string by using the specified provider to control the formatting of the specified interpolated string.</summary>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <param name="initialBuffer">The initial buffer that may be used as temporary space as part of the formatting operation. The contents of this buffer may be overwritten.</param>
        /// <param name="handler">The interpolated string.</param>
        /// <returns>The string that results for formatting the interpolated string using the specified format provider.</returns>
        public static string Create(
            IFormatProvider? provider,
            Span<char> initialBuffer,
            [InterpolatedStringHandlerArgument(nameof(provider), nameof(initialBuffer))] ref DefaultInterpolatedStringHandler handler) =>
            handler.ToStringAndClear();

        public static implicit operator ReadOnlySpan<char>(String? value)
        {
            ref char r = ref value.GetRawStringData();
            return new ReadOnlySpan<char>(ref r, value.Length);
        }
        public string Substring(int startIndex) => Substring(startIndex, Length - startIndex);
        public string Substring(int startIndex, int length)
        {
            if ((uint)startIndex > (uint)Length) throw new ArgumentOutOfRangeException("startIndex");
            if (length < 0) throw new ArgumentOutOfRangeException("length");
            if (startIndex + length > Length) throw new ArgumentOutOfRangeException("length");

            if (length == 0) return Empty;
            if (startIndex == 0 && length == Length) return this;

            string dstStr = FastAllocateString(length);
            ref char dst = ref dstStr.GetRawStringData();
            ref char src = ref GetRawStringData();

            for (int i = 0; i < length; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) =
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, startIndex + i);

            return dstStr;
        }
        public int IndexOf(char value) => IndexOf(value, 0, Length);

        public int IndexOf(char value, int startIndex)
        {
            if ((uint)startIndex > (uint)Length) throw new ArgumentOutOfRangeException("startIndex");
            return IndexOf(value, startIndex, Length - startIndex);
        }

        public int IndexOf(char value, int startIndex, int count)
        {
            int len = Length;
            if ((uint)startIndex > (uint)len) throw new ArgumentOutOfRangeException("startIndex");
            if (count < 0 || startIndex > len - count) throw new ArgumentOutOfRangeException("count");

            int end = startIndex + count;
            ref char src = ref GetRawStringData();

            for (int i = startIndex; i < end; i++)
            {
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i) == value)
                    return i;
            }

            return -1;
        }
        public int IndexOf(string value) => IndexOf(value, 0, Length);

        public int IndexOf(string value, int startIndex)
        {
            if ((object)value == null) throw new ArgumentNullException("value");
            if ((uint)startIndex > (uint)Length) throw new ArgumentOutOfRangeException("startIndex");

            return IndexOf(value, startIndex, Length - startIndex);
        }

        public int IndexOf(string value, int startIndex, int count)
        {
            if ((object)value == null) throw new ArgumentNullException("value");

            int n = Length;
            if ((uint)startIndex > (uint)n) throw new ArgumentOutOfRangeException("startIndex");
            if (count < 0 || startIndex > n - count) throw new ArgumentOutOfRangeException("count");

            int m = value.Length;
            if (m == 0) return startIndex;
            if (m == 1) return IndexOf(value[0], startIndex, count);
            if (m > count) return -1;

            ref char a = ref GetRawStringData();
            ref char b = ref value.GetRawStringData();

            int last = startIndex + count - m;
            char b0 = System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, 0);

            for (int i = startIndex; i <= last; i++)
            {
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, i) != b0)
                    continue;

                int j = 1;
                for (; j < m; j++)
                {
                    if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, i + j) !=
                        System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, j))
                        break;
                }

                if (j == m)
                    return i;
            }

            return -1;
        }

        public int LastIndexOf(char value)
        {
            int n = Length;
            if (n == 0) return -1;

            ref char src = ref GetRawStringData();
            for (int i = n - 1; i >= 0; i--)
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i) == value)
                    return i;

            return -1;
        }

        public int LastIndexOf(char value, int startIndex) => LastIndexOf(value, startIndex, startIndex + 1);

        public int LastIndexOf(char value, int startIndex, int count)
        {
            int len = Length;
            if (len == 0) return -1;

            if ((uint)startIndex >= (uint)len) throw new ArgumentOutOfRangeException("startIndex");
            if (count < 0) throw new ArgumentOutOfRangeException("count");
            if ((uint)count > (uint)startIndex + 1u) throw new ArgumentOutOfRangeException("count");

            int startSearchAt = startIndex + 1 - count;

            ref char src = ref GetRawStringData();
            for (int i = startIndex; i >= startSearchAt; i--)
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i) == value)
                    return i;

            return -1;
        }

        public int LastIndexOf(string value) => LastIndexOf(value, Length - 1, Length);

        public int LastIndexOf(string value, int startIndex) => LastIndexOf(value, startIndex, startIndex + 1);

        public int LastIndexOf(string value, int startIndex, int count)
        {
            if ((object)value == null) throw new ArgumentNullException("value");

            int thisLen = Length;
            int valueLen = value.Length;

            if (valueLen == 0)
            {
                if (thisLen == 0)
                {
                    if (startIndex < -1 || startIndex > 0) throw new ArgumentOutOfRangeException("startIndex");
                    if (count < 0) throw new ArgumentOutOfRangeException("count");
                    if (count > 1) throw new ArgumentOutOfRangeException("count");
                    return 0;
                }

                if (count < 0) throw new ArgumentOutOfRangeException("count");
                if (startIndex < 0 || startIndex > thisLen) throw new ArgumentOutOfRangeException("startIndex");

                if (startIndex == thisLen) startIndex = thisLen - 1;
                if (count > startIndex + 1) throw new ArgumentOutOfRangeException("count");

                return startIndex + 1;
            }

            if (thisLen == 0) return -1;

            if (count < 0) throw new ArgumentOutOfRangeException("count");
            if (startIndex < 0 || startIndex > thisLen) throw new ArgumentOutOfRangeException("startIndex");
            if (startIndex == thisLen) startIndex = thisLen - 1;
            if (count > startIndex + 1) throw new ArgumentOutOfRangeException("count");

            int searchStart = startIndex + 1 - count;

            if (valueLen > count) return -1;

            ref char a = ref GetRawStringData();
            ref char b = ref value.GetRawStringData();

            int last = startIndex - valueLen + 1;
            for (int i = last; i >= searchStart; i--)
            {
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, i) !=
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, 0))
                    continue;

                int j = 1;
                for (; j < valueLen; j++)
                {
                    if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, i + j) !=
                        System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, j))
                        break;
                }
                if (j == valueLen) return i;
            }

            return -1;
        }

        public bool StartsWith(string value)
        {
            if ((object)value == null) throw new ArgumentNullException("value");
            int n = value.Length;
            if (n > Length) return false;

            ref char a = ref GetRawStringData();
            ref char b = ref value.GetRawStringData();
            for (int i = 0; i < n; i++)
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, i) !=
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, i))
                    return false;
            return true;
        }
        public bool EndsWith(string value)
        {
            if ((object)value == null) throw new ArgumentNullException("value");
            int n = value.Length;
            int len = Length;
            if (n > len) return false;

            ref char a = ref GetRawStringData();
            ref char b = ref value.GetRawStringData();
            int start = len - n;
            for (int i = 0; i < n; i++)
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, start + i) !=
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, i))
                    return false;
            return true;
        }
        public string Replace(char oldChar, char newChar)
        {
            int n = Length;
            if (n == 0) return this;

            // Find first occurrence
            ref char src = ref GetRawStringData();
            int first = -1;
            for (int i = 0; i < n; i++)
            {
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i) == oldChar)
                {
                    first = i;
                    break;
                }
            }
            if (first < 0) return this;

            string dstStr = FastAllocateString(n);
            ref char dst = ref dstStr.GetRawStringData();

            for (int i = 0; i < n; i++)
            {
                char c = System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = (c == oldChar) ? newChar : c;
            }

            return dstStr;
        }
        public string Replace(string oldValue, string newValue)
        {
            if ((object)oldValue == null) throw new ArgumentNullException("oldValue");
            if ((object)newValue == null) newValue = Empty;

            int oldLen = oldValue.Length;
            if (oldLen == 0) throw new ArgumentException("oldValue cannot be empty.", "oldValue");

            int len = Length;
            if (len == 0) return this;

            int first = IndexOf(oldValue, 0);
            if (first < 0) return this;

            int newLen = newValue.Length;

            // Count occurrences
            int count = 0;
            int idx = first;
            while (idx >= 0)
            {
                count++;
                idx = IndexOf(oldValue, idx + oldLen);
            }

            long resultLen = (long)len + (long)count * ((long)newLen - (long)oldLen);
            if (resultLen <= 0) return Empty;
            if (resultLen > int.MaxValue) throw new OutOfMemoryException();

            string dstStr = FastAllocateString((int)resultLen);
            ref char dst = ref dstStr.GetRawStringData();

            ref char src = ref GetRawStringData();
            ref char ov = ref oldValue.GetRawStringData();

            int srcPos = 0;
            int dstPos = 0;
            int match = first;

            while (match >= 0)
            {
                // copy segment before match
                int segLen = match - srcPos;
                for (int i = 0; i < segLen; i++)
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, dstPos + i) =
                        System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, srcPos + i);

                dstPos += segLen;

                // copy replacement
                if (newLen != 0)
                {
                    CopyTo(newValue, ref dst, dstPos);
                    dstPos += newLen;
                }

                srcPos = match + oldLen;
                match = IndexOf(oldValue, srcPos);
            }

            // copy tail
            int tail = len - srcPos;
            for (int i = 0; i < tail; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, dstPos + i) =
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, srcPos + i);

            return dstStr;
        }
        public char[] ToCharArray()
        {
            int n = Length;
            var a = new char[n];
            ref char src = ref GetRawStringData();
            for (int i = 0; i < n; i++)
                a[i] = System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
            return a;
        }

        private static string ObjToString(object o)
        {
            if (o == null) return Empty;
            var s = o as string;
            if (s != null) return s;
            return o.ToString();
        }
        private static void CopyTo(string srcStr, ref char dst, int dstIndex)
        {
            int len = srcStr.Length;
            if (len == 0) return;

            ref char src = ref srcStr.GetRawStringData();
            for (int i = 0; i < len; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, dstIndex + i) =
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
        }

        public static string Concat(object a, object b)
        {
            string s0 = ObjToString(a);
            string s1 = ObjToString(b);
            return Concat(s0, s1);
        }
        public static string Concat(object a, object b, object c)
        {
            string s0 = ObjToString(a);
            string s1 = ObjToString(b);
            string s2 = ObjToString(c);
            return Concat(s0, s1, s2);
        }
        public static string Concat(object a, object b, object c, object d)
        {
            string s0 = ObjToString(a);
            string s1 = ObjToString(b);
            string s2 = ObjToString(c);
            string s3 = ObjToString(d);
            return Concat(s0, s1, s2, s3);
        }
        public static string Concat(object[] values)
        {
            if (values == null) throw new ArgumentNullException("values");

            int n = values.Length;
            if (n == 0) return Empty;

            var parts = new string[n];
            int total = 0;

            for (int i = 0; i < n; i++)
            {
                string s = ObjToString(values[i]);
                parts[i] = s;
                total += s.Length;
            }

            if (total == 0) return Empty;

            string dstStr = FastAllocateString(total);
            ref char dst = ref dstStr.GetRawStringData();

            int pos = 0;
            for (int i = 0; i < n; i++)
            {
                string s = parts[i];
                int len = s.Length;
                if (len != 0)
                {
                    ref char src = ref s.GetRawStringData();
                    for (int k = 0; k < len; k++)
                        System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, pos + k) =
                            System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, k);
                    pos += len;
                }
            }

            return dstStr;
        }
        public static string Concat(string a, string b)
        {
            if ((object)a == null) a = Empty;
            if ((object)b == null) b = Empty;

            int la = a.Length;
            int lb = b.Length;
            int total = la + lb;
            if (total == 0) return Empty;

            string dstStr = FastAllocateString(total);
            ref char dst = ref dstStr.GetRawStringData();

            int pos = 0;
            if (la != 0)
            {
                ref char src = ref a.GetRawStringData();
                for (int i = 0; i < la; i++)
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) =
                        System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
                pos = la;
            }
            if (lb != 0)
            {
                ref char src = ref b.GetRawStringData();
                for (int i = 0; i < lb; i++)
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, pos + i) =
                        System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
            }

            return dstStr;
        }
        public static string Concat(string a, string b, string c)
            => Concat(Concat(a, b), c);
        public static string Concat(string a, string b, string c, string d)
            => Concat(Concat(a, b), Concat(c, d));

        public static string Join(char separator, string[] value)
            => Join(separator.ToString(), value);

        public static string Join(char separator, object[] values)
            => Join(separator.ToString(), values);

        public static string Join(string separator, string[] value)
        {
            if ((object)value == null) throw new ArgumentNullException("value");
            if ((object)separator == null) separator = Empty;

            int n = value.Length;
            if (n == 0) return Empty;
            if (n == 1)
            {
                string s0 = value[0];
                return (object)s0 == null ? Empty : s0;
            }

            int sepLen = separator.Length;
            long total = 0;

            for (int i = 0; i < n; i++)
            {
                string s = value[i];
                if ((object)s != null) total += s.Length;
            }
            total += (long)sepLen * (n - 1);

            if (total <= 0) return Empty;
            if (total > int.MaxValue) throw new OutOfMemoryException();

            string dstStr = FastAllocateString((int)total);
            ref char dst = ref dstStr.GetRawStringData();

            int pos = 0;
            for (int i = 0; i < n; i++)
            {
                if (i != 0 && sepLen != 0)
                {
                    CopyTo(separator, ref dst, pos);
                    pos += sepLen;
                }

                string s = value[i];
                if ((object)s != null && s.Length != 0)
                {
                    CopyTo(s, ref dst, pos);
                    pos += s.Length;
                }
            }

            return dstStr;
        }
        public static string Join(string separator, object[] values)
        {
            if ((object)values == null) throw new ArgumentNullException("values");
            if ((object)separator == null) separator = Empty;

            int n = values.Length;
            if (n == 0) return Empty;
            if (n == 1) return ObjToString(values[0]);

            int sepLen = separator.Length;
            var parts = new string[n];
            long total = 0;

            for (int i = 0; i < n; i++)
            {
                string s = ObjToString(values[i]);
                parts[i] = s;
                total += s.Length;
            }
            total += (long)sepLen * (n - 1);

            if (total <= 0) return Empty;
            if (total > int.MaxValue) throw new OutOfMemoryException();

            string dstStr = FastAllocateString((int)total);
            ref char dst = ref dstStr.GetRawStringData();

            int pos = 0;
            for (int i = 0; i < n; i++)
            {
                if (i != 0 && sepLen != 0)
                {
                    CopyTo(separator, ref dst, pos);
                    pos += sepLen;
                }

                string s = parts[i];
                if (s.Length != 0)
                {
                    CopyTo(s, ref dst, pos);
                    pos += s.Length;
                }
            }

            return dstStr;
        }


        public string[] Split(char separator, StringSplitOptions options = StringSplitOptions.None)
        {
            return SplitInternal(new ReadOnlySpan<char>(in separator), int.MaxValue, options);
        }

        public string[] Split(char separator, int count, StringSplitOptions options = StringSplitOptions.None)
        {
            return SplitInternal(new ReadOnlySpan<char>(in separator), count, options);
        }
        public string[] Split(char[] separator, int count, StringSplitOptions options = StringSplitOptions.None)
        {
            return SplitInternal(new ReadOnlySpan<char>(separator), count, options);
        }
        public string[] Split(char[] separator, StringSplitOptions options)
        {
            return SplitInternal(new ReadOnlySpan<char>(separator), int.MaxValue, options);
        }
        public string[] Split(char[] separator, int count)
        {
            return SplitInternal(separator, count, StringSplitOptions.None);
        }

        public string[] Split(string? separator, StringSplitOptions options = StringSplitOptions.None)
        {
            return SplitInternal(separator ?? Empty, null, int.MaxValue, options);
        }

        public string[] Split(string? separator, int count, StringSplitOptions options = StringSplitOptions.None)
        {
            return SplitInternal(separator ?? Empty, null, count, options);
        }

        public string[] Split(string[]? separator, StringSplitOptions options)
        {
            return SplitInternal(null, separator, int.MaxValue, options);
        }

        public string[] Split(string[]? separator, int count, StringSplitOptions options)
        {
            return SplitInternal(null, separator, count, options);
        }

        private static void CheckStringSplitOptions(StringSplitOptions options)
        {
            const StringSplitOptions All =
                StringSplitOptions.None |
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries;

            if ((options & ~All) != 0)
                throw new ArgumentException("options");
        }
        private string[] SplitInternal(string separator, int count, StringSplitOptions options)
        {
            if (count <= 1 || Length == 0)
            {
                return CreateSplitArrayOfThisAsSoleValue(options, count);
            }

            int[] sepListArray = MakeSeparatorList(this, separator, out int sepCount);
            if (sepCount == 0)
            {
                return CreateSplitArrayOfThisAsSoleValue(options, count);
            }

            ReadOnlySpan<int> sepList = new ReadOnlySpan<int>(sepListArray).Slice(0, sepCount);

            return (options != StringSplitOptions.None)
                ? SplitWithPostProcessing(sepList, default, separator.Length, count, options)
                : SplitWithoutPostProcessing(sepList, default, separator.Length, count);
        }
        private string[] SplitInternal(string? separator, string?[]? separators, int count, StringSplitOptions options)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException("count");

            CheckStringSplitOptions(options);

            bool singleSeparator = separator != null;

            if (!singleSeparator && (separators == null || separators.Length == 0))
            {
                // split on whitespace
                return SplitInternal(default(ReadOnlySpan<char>), count, options);
            }

        ShortCircuit:
            if (count <= 1 || Length == 0)
            {
                // Per the method's documentation, we'll short-circuit the search for separators.
                // But we still need to post-process the results based on the caller-provided flags.
                return CreateSplitArrayOfThisAsSoleValue(options, count);
            }

            if (singleSeparator)
            {
                if (separator.Length == 0)
                {
                    count = 1;
                    goto ShortCircuit;
                }
                else
                {
                    return SplitInternal(separator, count, options);
                }
            }

            int[] sepListArray;
            int[] lengthListArray;
            int sepCount;

            MakeSeparatorListAny(this, separators, out sepListArray, out lengthListArray, out sepCount);

            ReadOnlySpan<int> sepList = new ReadOnlySpan<int>(sepListArray).Slice(0, sepCount);
            ReadOnlySpan<int> lengthList = new ReadOnlySpan<int>(lengthListArray).Slice(0, sepCount);

            if (sepList.Length == 0)
            {
                return CreateSplitArrayOfThisAsSoleValue(options, count);
            }

            string[] result = (options != StringSplitOptions.None)
                ? SplitWithPostProcessing(sepList, lengthList, 0, count, options)
                : SplitWithoutPostProcessing(sepList, lengthList, 0, count);

            return result;
        }
        private string[] SplitInternal(ReadOnlySpan<char> separators, int count, StringSplitOptions options)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException("count");

            CheckStringSplitOptions(options);

        ShortCircuit:
            if (count <= 1 || Length == 0)
            {
                // Per the method's documentation, we'll short-circuit the search for separators.
                // But we still need to post-process the results based on the caller-provided flags.
                return CreateSplitArrayOfThisAsSoleValue(options, count);
            }

            if (separators.IsEmpty && count > Length)
            {
                // Caller is already splitting on whitespace; no need for separate trim step if the count is sufficient
                // to examine the whole input.
                options &= ~StringSplitOptions.TrimEntries;
            }

            int[] sepListArray = MakeSeparatorListAny(this, separators, out int sepCount);
            if (sepCount == 0)
            {
                count = 1;
                goto ShortCircuit;
            }

            ReadOnlySpan<int> sepList = new ReadOnlySpan<int>(sepListArray).Slice(0, sepCount);

            string[] result = (options != StringSplitOptions.None)
                ? SplitWithPostProcessing(sepList, default, 1, count, options)
                : SplitWithoutPostProcessing(sepList, default, 1, count);

            return result;
        }
        private static bool IsMatchSeparator(char c, ReadOnlySpan<char> separators)
        {
            if (separators.IsEmpty)
                return Char.IsWhiteSpace(c);

            for (int i = 0; i < separators.Length; i++)
            {
                if (c == separators[i])
                    return true;
            }

            return false;
        }
        private static int[] MakeSeparatorListAny(string source, ReadOnlySpan<char> separators, out int count)
        {
            int len = source.Length;
            count = 0;

            if (len == 0)
                return Array.Empty<int>();

            ref char src = ref source.GetRawStringData();

            // count separators
            for (int i = 0; i < len; i++)
            {
                char c = System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
                if (IsMatchSeparator(c, separators))
                    count++;
            }

            if (count == 0)
                return Array.Empty<int>();

            int[] result = new int[count];
            int pos = 0;

            // write separator indices
            for (int i = 0; i < len; i++)
            {
                char c = System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
                if (IsMatchSeparator(c, separators))
                    result[pos++] = i;
            }

            return result;
        }
        private static bool MatchStringSeparatorAt(string source, int index, string separator)
        {
            int sepLen = separator.Length;
            if (sepLen == 0)
                return false;

            if (index > source.Length - sepLen)
                return false;

            for (int i = 0; i < sepLen; i++)
            {
                if (source[index + i] != separator[i])
                    return false;
            }

            return true;
        }

        private static int[] MakeSeparatorList(string source, string separator, out int count)
        {
            count = 0;

            int sourceLength = source.Length;
            int sepLen = separator.Length;

            if (sourceLength == 0 || sepLen == 0 || sepLen > sourceLength)
                return Array.Empty<int>();

            // count non overlapping matches
            for (int i = 0; i <= sourceLength - sepLen; i++)
            {
                if (MatchStringSeparatorAt(source, i, separator))
                {
                    count++;
                    i += sepLen - 1;
                }
            }

            if (count == 0)
                return Array.Empty<int>();

            int[] sepList = new int[count];
            int pos = 0;

            // record match positions
            for (int i = 0; i <= sourceLength - sepLen; i++)
            {
                if (MatchStringSeparatorAt(source, i, separator))
                {
                    sepList[pos++] = i;
                    i += sepLen - 1;
                }
            }

            return sepList;
        }

        private static bool TryMatchAnySeparatorAt(string source, int index, string?[] separators, out int matchedLength)
        {
            for (int s = 0; s < separators.Length; s++)
            {
                string sep = separators[s];
                if ((object)sep == null || sep.Length == 0)
                    continue;

                if (MatchStringSeparatorAt(source, index, sep))
                {
                    matchedLength = sep.Length;
                    return true;
                }
            }

            matchedLength = 0;
            return false;
        }

        private static void MakeSeparatorListAny(
            string source,
            string?[] separators,
            out int[] sepList,
            out int[] lengthList,
            out int count)
        {
            count = 0;

            int sourceLength = source.Length;
            if (sourceLength == 0)
            {
                sepList = Array.Empty<int>();
                lengthList = Array.Empty<int>();
                return;
            }

            // count matches
            for (int i = 0; i < sourceLength; i++)
            {
                if (TryMatchAnySeparatorAt(source, i, separators, out int matchedLength))
                {
                    count++;
                    i += matchedLength - 1;
                }
            }

            if (count == 0)
            {
                sepList = Array.Empty<int>();
                lengthList = Array.Empty<int>();
                return;
            }

            sepList = new int[count];
            lengthList = new int[count];

            int pos = 0;

            // record positions and lengths
            for (int i = 0; i < sourceLength; i++)
            {
                if (TryMatchAnySeparatorAt(source, i, separators, out int matchedLength))
                {
                    sepList[pos] = i;
                    lengthList[pos] = matchedLength;
                    pos++;
                    i += matchedLength - 1;
                }
            }
        }
        private string[] CreateSplitArrayOfThisAsSoleValue(StringSplitOptions options, int count)
        {
            if (count != 0)
            {
                string candidate = this;

                if ((options & StringSplitOptions.TrimEntries) != 0)
                {
                    candidate = candidate.Trim();
                }

                if ((options & StringSplitOptions.RemoveEmptyEntries) == 0 || candidate.Length != 0)
                {
                    return new string[] { candidate };
                }
            }

            return Array.Empty<string>();
        }
        // This function may trim entries or omit empty entries
        private string[] SplitWithPostProcessing(ReadOnlySpan<int> sepList, ReadOnlySpan<int> lengthList, int defaultLength, int count, StringSplitOptions options)
        {
            int numReplaces = sepList.Length;

            // Allocate array to hold items. This array may not be
            // filled completely in this function, we will create a
            // new array and copy string references to that new array.
            int maxItems = (numReplaces < count) ? (numReplaces + 1) : count;
            string[] splitStrings = new string[maxItems];

            int currIndex = 0;
            int arrIndex = 0;

            ReadOnlySpan<char> thisEntry;

            for (int i = 0; i < numReplaces; i++)
            {
                thisEntry = this.AsSpan(currIndex, sepList[i] - currIndex);
                if ((options & StringSplitOptions.TrimEntries) != 0)
                {
                    thisEntry = thisEntry.Trim();
                }
                if (!thisEntry.IsEmpty || ((options & StringSplitOptions.RemoveEmptyEntries) == 0))
                {
                    splitStrings[arrIndex++] = thisEntry.ToString();
                }
                currIndex = sepList[i] + (lengthList.IsEmpty ? defaultLength : lengthList[i]);
                if (arrIndex == count - 1)
                {
                    // The next iteration of the loop will provide the final entry into the
                    // results array. If needed, skip over all empty entries before that
                    // point.
                    if ((options & StringSplitOptions.RemoveEmptyEntries) != 0)
                    {
                        while (++i < numReplaces)
                        {
                            thisEntry = this.AsSpan(currIndex, sepList[i] - currIndex);
                            if ((options & StringSplitOptions.TrimEntries) != 0)
                            {
                                thisEntry = thisEntry.Trim();
                            }
                            if (!thisEntry.IsEmpty)
                            {
                                break; // there's useful data here
                            }
                            currIndex = sepList[i] + (lengthList.IsEmpty ? defaultLength : lengthList[i]);
                        }
                    }
                    break;
                }
            }


            // Handle the last substring at the end of the array
            // (could be empty if separator appeared at the end of the input string)
            thisEntry = this.AsSpan(currIndex);
            if ((options & StringSplitOptions.TrimEntries) != 0)
            {
                thisEntry = thisEntry.Trim();
            }
            if (!thisEntry.IsEmpty || ((options & StringSplitOptions.RemoveEmptyEntries) == 0))
            {
                splitStrings[arrIndex++] = thisEntry.ToString();
            }

            Array.Resize<string>(ref splitStrings, arrIndex);
            return splitStrings;
        }
        // This function will not trim entries or special-case empty entries
        private string[] SplitWithoutPostProcessing(ReadOnlySpan<int> sepList, ReadOnlySpan<int> lengthList, int defaultLength, int count)
        {
            int currIndex = 0;
            int arrIndex = 0;

            count--;
            int numActualReplaces = (sepList.Length < count) ? sepList.Length : count;

            // Allocate space for the new array.
            // +1 for the string from the end of the last replace to the end of the string.
            string[] splitStrings = new string[numActualReplaces + 1];

            for (int i = 0; i < numActualReplaces && currIndex < Length; i++)
            {
                splitStrings[arrIndex++] = Substring(currIndex, sepList[i] - currIndex);
                currIndex = sepList[i] + (lengthList.IsEmpty ? defaultLength : lengthList[i]);
            }

            // Handle the last string at the end of the array if there is one.
            if (currIndex < Length && numActualReplaces >= 0)
            {
                splitStrings[arrIndex] = Substring(currIndex);
            }
            else if (arrIndex == numActualReplaces)
            {
                // We had a separator character at the end of a string.  Rather than just allowing
                // a null character, we'll replace the last element in the array with an empty string.
                splitStrings[arrIndex] = Empty;
            }

            return splitStrings;
        }


        public string TrimStart()
        {
            int len = Length;
            if (len == 0) return this;

            int i = 0;
            while (i < len && Char.IsWhiteSpace(this[i])) i++;

            if (i == 0) return this;
            if (i == len) return Empty;
            return Substring(i);
        }

        public string TrimEnd()
        {
            int len = Length;
            if (len == 0) return this;

            int i = len - 1;
            while (i >= 0 && Char.IsWhiteSpace(this[i])) i--;

            if (i == len - 1) return this;
            if (i < 0) return Empty;
            return Substring(0, i + 1);
        }

        public string Trim()
        {
            int len = Length;
            if (len == 0) return this;

            int start = 0;
            while (start < len && Char.IsWhiteSpace(this[start])) start++;
            if (start == len) return Empty;

            int end = len - 1;
            while (end >= start && Char.IsWhiteSpace(this[end])) end--;

            if (start == 0 && end == len - 1) return this;
            return Substring(start, end - start + 1);
        }

        public string PadLeft(int totalWidth) => PadLeft(totalWidth, ' ');
        public string PadLeft(int totalWidth, char paddingChar)
        {
            if (totalWidth < 0) throw new ArgumentOutOfRangeException("totalWidth");

            int oldLength = Length;
            int padCount = totalWidth - oldLength;
            if (padCount <= 0) return this;

            string result = FastAllocateString(totalWidth);
            ref char dst = ref result.GetRawStringData();

            for (int i = 0; i < padCount; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = paddingChar;

            ref char src = ref GetRawStringData();
            for (int i = 0; i < oldLength; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, padCount + i) =
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);

            return result;
        }

        public string PadRight(int totalWidth) => PadRight(totalWidth, ' ');
        public string PadRight(int totalWidth, char paddingChar)
        {
            if (totalWidth < 0) throw new ArgumentOutOfRangeException("totalWidth");

            int oldLength = Length;
            int padCount = totalWidth - oldLength;
            if (padCount <= 0) return this;

            string result = FastAllocateString(totalWidth);
            ref char dst = ref result.GetRawStringData();

            ref char src = ref GetRawStringData();
            for (int i = 0; i < oldLength; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) =
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);

            for (int i = 0; i < padCount; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, oldLength + i) = paddingChar;

            return result;
        }
        public bool Contains(char value) => IndexOf(value) >= 0;

        public bool Contains(string value)
        {
            if ((object)value == null) throw new ArgumentNullException("value");
            return IndexOf(value, 0) >= 0;
        }

        public string ToLower() => ToLowerInvariant();
        public string ToLowerInvariant()
        {
            return System.Globalization.TextInfo.Invariant.ToLower(this);
        }
        public string ToUpper() => ToUpperInvariant();
        public string ToUpperInvariant()
        {
            return System.Globalization.TextInfo.Invariant.ToUpper(this);
        }



        private static unsafe int CompareOrdinalHelper(string strA, string strB)
        {
            int length = Math.Min(strA.Length, strB.Length);

            fixed (char* ap = &strA._firstChar) fixed (char* bp = &strB._firstChar)
            {
                char* a = ap;
                char* b = bp;

                if (*(a + 1) != *(b + 1)) goto DiffOffset1;

                length -= 2; a += 2; b += 2;

                // unroll the loop
#if TARGET_64BIT
                while (length >= 12)
                {
                    if (*(long*)a != *(long*)b) goto DiffOffset0;
                    if (*(long*)(a + 4) != *(long*)(b + 4)) goto DiffOffset4;
                    if (*(long*)(a + 8) != *(long*)(b + 8)) goto DiffOffset8;
                    length -= 12; a += 12; b += 12;
                }
#else // TARGET_64BIT
                while (length >= 10)
                {
                    if (*(int*)a != *(int*)b) goto DiffOffset0;
                    if (*(int*)(a + 2) != *(int*)(b + 2)) goto DiffOffset2;
                    if (*(int*)(a + 4) != *(int*)(b + 4)) goto DiffOffset4;
                    if (*(int*)(a + 6) != *(int*)(b + 6)) goto DiffOffset6;
                    if (*(int*)(a + 8) != *(int*)(b + 8)) goto DiffOffset8;
                    length -= 10; a += 10; b += 10;
                }
#endif // TARGET_64BIT

                // Fallback loop:
                // go back to slower code path and do comparison on 4 bytes at a time.
                // This depends on the fact that the String objects are
                // always zero terminated and that the terminating zero is not included
                // in the length. For odd string sizes, the last compare will include
                // the zero terminator.
                while (length > 0)
                {
                    if (*(int*)a != *(int*)b) goto DiffNextInt;
                    length -= 2;
                    a += 2;
                    b += 2;
                }

                // At this point, we have compared all the characters in at least one string.
                // The longer string will be larger.
                return strA.Length - strB.Length;

#if TARGET_64BIT
            DiffOffset8: a += 4; b += 4;
            DiffOffset4: a += 4; b += 4;
#else // TARGET_64BIT
                // Use jumps instead of falling through, since
                // otherwise going to DiffOffset8 will involve
                // 8 add instructions before getting to DiffNextInt
            DiffOffset8: a += 8; b += 8; goto DiffOffset0;
            DiffOffset6: a += 6; b += 6; goto DiffOffset0;
            DiffOffset4: a += 2; b += 2;
            DiffOffset2: a += 2; b += 2;
#endif // TARGET_64BIT

            DiffOffset0:
                // If we reached here, we already see a difference in the unrolled loop above
#if TARGET_64BIT
                if (*(int*)a == *(int*)b)
                {
                    a += 2; b += 2;
                }
#endif // TARGET_64BIT

            DiffNextInt:
                if (*a != *b) return *a - *b;

            DiffOffset1:
                return *(a + 1) - *(b + 1);
            }
        }

        public static int CompareOrdinal(string? strA, string? strB)
        {
            if (object.ReferenceEquals(strA, strB))
            {
                return 0;
            }

            // They can't both be null at this point.
            if (strA == null)
            {
                return -1;
            }
            if (strB == null)
            {
                return 1;
            }

            // Most common case, first character is different.
            // This will return false for empty strings.
            if (strA._firstChar != strB._firstChar)
            {
                return strA._firstChar - strB._firstChar;
            }

            return CompareOrdinalHelper(strA, strB);
        }

        public static int CompareOrdinal(string? strA, int indexA, string? strB, int indexB, int length)
        {
            if (strA == null || strB == null)
            {
                if (object.ReferenceEquals(strA, strB))
                {
                    // They're both null
                    return 0;
                }

                return strA == null ? -1 : 1;
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            if (indexA < 0 || indexB < 0)
            {
                string paramName = indexA < 0 ? nameof(indexA) : nameof(indexB);
                throw new ArgumentOutOfRangeException(paramName);
            }

            int lengthA = Math.Min(length, strA.Length - indexA);
            int lengthB = Math.Min(length, strB.Length - indexB);

            if (lengthA < 0 || lengthB < 0)
            {
                string paramName = lengthA < 0 ? nameof(indexA) : nameof(indexB);
                throw new ArgumentOutOfRangeException(paramName);
            }

            if (length == 0 || (object.ReferenceEquals(strA, strB) && indexA == indexB))
            {
                return 0;
            }

            return CompareOrdinalHelper(strA, indexA, lengthA, strB, indexB, lengthB);
        }
    }

    public struct Boolean
    {
        private readonly bool m_value;
        internal const int True = 1;
        internal const int False = 0;
        internal const string TrueLiteral = "True";
        internal const string FalseLiteral = "False";
        public override string ToString()
        {
            return m_value ? TrueLiteral : FalseLiteral;
        }
    }
    public struct Char
    {
        private readonly char m_value;
        private const byte IsWhiteSpaceFlag = 0x80;
        private const byte IsUpperCaseLetterFlag = 0x40;
        private const byte IsLowerCaseLetterFlag = 0x20;
        private const byte UnicodeCategoryMask = 0x1F;
        public const char MaxValue = (char)0xFFFF;
        public const char MinValue = (char)0x00;

        public bool Equals(char obj)
        {
            return m_value == obj;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is char))
            {
                return false;
            }
            return m_value == ((char)obj).m_value;
        }
        public override int GetHashCode()
        {
            return (int)m_value | ((int)m_value << 16);
        }
        public override string ToString()
        {
            return System.Number.CharToString(m_value);
        }

        private static ReadOnlySpan<byte> Latin1CharInfo =>
        [
        //  0     1     2     3     4     5     6     7     8     9     A     B     C     D     E     F
            0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x8E, 0x8E, 0x8E, 0x8E, 0x8E, 0x0E, 0x0E, // U+0000..U+000F
            0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, // U+0010..U+001F
            0x8B, 0x18, 0x18, 0x18, 0x1A, 0x18, 0x18, 0x18, 0x14, 0x15, 0x18, 0x19, 0x18, 0x13, 0x18, 0x18, // U+0020..U+002F
            0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x18, 0x18, 0x19, 0x19, 0x19, 0x18, // U+0030..U+003F
            0x18, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, // U+0040..U+004F
            0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x14, 0x18, 0x15, 0x1B, 0x12, // U+0050..U+005F
            0x1B, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, // U+0060..U+006F
            0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x14, 0x19, 0x15, 0x19, 0x0E, // U+0070..U+007F
            0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x8E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, // U+0080..U+008F
            0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, 0x0E, // U+0090..U+009F
            0x8B, 0x18, 0x1A, 0x1A, 0x1A, 0x1A, 0x1C, 0x18, 0x1B, 0x1C, 0x04, 0x16, 0x19, 0x0F, 0x1C, 0x1B, // U+00A0..U+00AF
            0x1C, 0x19, 0x0A, 0x0A, 0x1B, 0x21, 0x18, 0x18, 0x1B, 0x0A, 0x04, 0x17, 0x0A, 0x0A, 0x0A, 0x18, // U+00B0..U+00BF
            0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, // U+00C0..U+00CF
            0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x19, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x21, // U+00D0..U+00DF
            0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, // U+00E0..U+00EF
            0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x19, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x21, // U+00F0..U+00FF
        ];

        public static bool IsBetween(char c, char minInclusive, char maxInclusive) =>
            (uint)(c - minInclusive) <= (uint)(maxInclusive - minInclusive);
        private static bool IsBetween(
            System.Globalization.UnicodeCategory c,
            System.Globalization.UnicodeCategory min,
            System.Globalization.UnicodeCategory max) =>
            (uint)(c - min) <= (uint)(max - min);
        public static bool IsAscii(char c) => (uint)c <= '\x007f';
        public static bool IsAsciiLetter(char c) => (uint)((c | 0x20) - 'a') <= 'z' - 'a';
        public static bool IsAsciiDigit(char c) => IsBetween(c, '0', '9');
        public static bool IsAsciiLetterOrDigit(char c) => IsAsciiLetter(c) | IsBetween(c, '0', '9');
        public static bool IsAsciiLetterLower(char c) => IsBetween(c, 'a', 'z');
        public static bool IsAsciiLetterUpper(char c) => IsBetween(c, 'A', 'Z');

        public static char ToLowerInvariant(char c) => System.Globalization.TextInfo.ToLowerInvariant(c);
        public static char ToUpperInvariant(char c) => System.Globalization.TextInfo.ToUpperInvariant(c);


        public static bool IsWhiteSpace(char c)
        {
            if (IsLatin1(c))
            {
                return IsWhiteSpaceLatin1(c);
            }
            //return CharUnicodeInfo.GetIsWhiteSpace(c);
            if (c == '\u1680') return true;
            if (c >= '\u2000' && c <= '\u200A') return true;
            if (c == '\u2028' || c == '\u2029') return true;
            if (c == '\u202F' || c == '\u205F') return true;
            if (c == '\u3000' || c == '\uFEFF') return true;
            return false;
        }

        private static bool IsLatin1(char c) => (uint)c < (uint)Latin1CharInfo.Length;
        private static bool IsWhiteSpaceLatin1(char c) => (Latin1CharInfo[c] & IsWhiteSpaceFlag) != 0;
        private static System.Globalization.UnicodeCategory GetLatin1UnicodeCategory(char c)
            => (System.Globalization.UnicodeCategory)(Latin1CharInfo[c] & UnicodeCategoryMask);

        public static bool IsHighSurrogate(char c)
        {
            return IsBetween(c, System.Globalization.CharUnicodeInfo.HIGH_SURROGATE_START, System.Globalization.CharUnicodeInfo.HIGH_SURROGATE_END);
        }

        public static bool IsHighSurrogate(string s, int index)
        {
            if (s == null)
            {
                throw new ArgumentNullException();
            }
            if ((uint)index >= (uint)s.Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            return IsHighSurrogate(s[index]);
        }
        public static bool IsLowSurrogate(char c)
        {
            return IsBetween(c, System.Globalization.CharUnicodeInfo.LOW_SURROGATE_START, System.Globalization.CharUnicodeInfo.LOW_SURROGATE_END);
        }

        public static bool IsLowSurrogate(string s, int index)
        {
            if (s == null)
            {
                throw new ArgumentNullException();
            }
            if ((uint)index >= (uint)s.Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            return IsLowSurrogate(s[index]);
        }

    }

    public readonly struct SByte
    {
        private readonly sbyte m_value;
        public const sbyte MaxValue = (sbyte)0x7F;
        public const sbyte MinValue = unchecked((sbyte)0x80);

        public bool Equals(sbyte obj)
        {
            return m_value == obj;
        }
        public override int GetHashCode()
        {
            return m_value;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is sbyte))
            {
                return false;
            }
            return m_value == ((sbyte)obj).m_value;
        }

        public static sbyte Parse(ReadOnlySpan<char> s)
        {
            sbyte r;
            var st = System.Number.TryParseSByte(s, out r);
            if (st == System.Number.ParseStatus.OK) return r;
            if (st == System.Number.ParseStatus.Overflow) throw new OverflowException();
            throw new FormatException();
        }

        public static bool TryParse(ReadOnlySpan<char> s, out sbyte result)
        {
            return System.Number.TryParseSByte(s, out result) == System.Number.ParseStatus.OK;
        }

        public static sbyte Parse(String str)
        {
            if ((object)str == null) throw new ArgumentNullException("str");
            return Parse(str.AsSpan());
        }

        public static bool TryParse(String str, out sbyte result)
        {
            if ((object)str == null) { result = 0; return false; }
            return TryParse(str.AsSpan(), out result);
        }

        public override string ToString()
        {
            return System.Number.Int32ToString((int)m_value);
        }
    }
    public readonly struct Byte
    {
        private readonly byte m_value;
        public const byte MaxValue = (byte)0xFF;
        public const byte MinValue = 0;

        public bool Equals(byte obj)
        {
            return m_value == obj;
        }
        public override int GetHashCode()
        {
            return m_value;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is byte))
            {
                return false;
            }
            return m_value == ((byte)obj).m_value;
        }

        public static byte Parse(ReadOnlySpan<char> s)
        {
            byte r;
            var st = System.Number.TryParseByte(s, out r);
            if (st == System.Number.ParseStatus.OK) return r;
            if (st == System.Number.ParseStatus.Overflow) throw new OverflowException();
            throw new FormatException();
        }

        public static bool TryParse(ReadOnlySpan<char> s, out byte result)
        {
            return System.Number.TryParseByte(s, out result) == System.Number.ParseStatus.OK;
        }

        public static byte Parse(String str)
        {
            if ((object)str == null) throw new ArgumentNullException("str");
            return Parse(str.AsSpan());
        }

        public static bool TryParse(String str, out byte result)
        {
            if ((object)str == null) { result = 0; return false; }
            return TryParse(str.AsSpan(), out result);
        }

        public override string ToString()
        {
            return System.Number.UInt32ToString((uint)m_value);
        }
    }
    public readonly struct Int16
    {
        private readonly short m_value;
        public const short MaxValue = (short)0x7FFF;
        public const short MinValue = unchecked((short)0x8000);

        public bool Equals(short obj)
        {
            return m_value == obj;
        }
        public override int GetHashCode()
        {
            return m_value;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is short))
            {
                return false;
            }
            return m_value == ((short)obj).m_value;
        }

        public static short Parse(ReadOnlySpan<char> s)
        {
            short r;
            var st = System.Number.TryParseInt16(s, out r);
            if (st == System.Number.ParseStatus.OK) return r;
            if (st == System.Number.ParseStatus.Overflow) throw new OverflowException();
            throw new FormatException();
        }

        public static bool TryParse(ReadOnlySpan<char> s, out short result)
        {
            return System.Number.TryParseInt16(s, out result) == System.Number.ParseStatus.OK;
        }

        public static short Parse(String str)
        {
            if ((object)str == null) throw new ArgumentNullException("str");
            return Parse(str.AsSpan());
        }

        public static bool TryParse(String str, out short result)
        {
            if ((object)str == null) { result = 0; return false; }
            return TryParse(str.AsSpan(), out result);
        }

        public override string ToString()
        {
            return System.Number.FormatInt32((int)m_value, 0, null, null);
        }
        public string ToString(string format)
        {
            return System.Number.FormatInt32((int)m_value, 0xFFFF, format, null);
        }
    }
    public readonly struct UInt16
    {
        private readonly ushort m_value;
        public const ushort MaxValue = (ushort)0xFFFF;
        public const ushort MinValue = 0;

        public bool Equals(ushort obj)
        {
            return m_value == obj;
        }
        public override int GetHashCode()
        {
            return (int)m_value;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is ushort))
            {
                return false;
            }
            return m_value == ((ushort)obj).m_value;
        }

        public static ushort Parse(ReadOnlySpan<char> s)
        {
            ushort r;
            var st = System.Number.TryParseUInt16(s, out r);
            if (st == System.Number.ParseStatus.OK) return r;
            if (st == System.Number.ParseStatus.Overflow) throw new OverflowException();
            throw new FormatException();
        }

        public static bool TryParse(ReadOnlySpan<char> s, out ushort result)
        {
            return System.Number.TryParseUInt16(s, out result) == System.Number.ParseStatus.OK;
        }

        public static ushort Parse(String str)
        {
            if ((object)str == null) throw new ArgumentNullException("str");
            return Parse(str.AsSpan());
        }

        public static bool TryParse(String str, out ushort result)
        {
            if ((object)str == null) { result = 0; return false; }
            return TryParse(str.AsSpan(), out result);
        }

        public override string ToString()
        {
            return System.Number.UInt32ToString((uint)m_value);
        }
        public string ToString(string format)
        {
            return System.Number.FormatInt32(m_value, 0, format, null);
        }
    }
    public readonly struct Int32
        : IComparable
    {
        private readonly int m_value; // Do not rename

        public const int MaxValue = 0x7fffffff;
        public const int MinValue = unchecked((int)0x80000000);

        /// <summary>Represents the additive identity (0).</summary>
        private const int AdditiveIdentity = 0;

        /// <summary>Represents the multiplicative identity (1).</summary>
        private const int MultiplicativeIdentity = 1;

        /// <summary>Represents the number one (1).</summary>
        private const int One = 1;

        /// <summary>Represents the number zero (0).</summary>
        private const int Zero = 0;

        /// <summary>Represents the number negative one (-1).</summary>
        private const int NegativeOne = -1;

        /// <summary>Produces the full product of two 32-bit numbers.</summary>
        /// <param name="left">The first number to multiply.</param>
        /// <param name="right">The second number to multiply.</param>
        /// <returns>The number containing the product of the specified numbers.</returns>
        public static long BigMul(int left, int right) => Math.BigMul(left, right);

        public int CompareTo(object? value)
        {
            if (value == null)
            {
                return 1;
            }

            // NOTE: Cannot use return (_value - value) as this causes a wrap
            // around in cases where _value - value > MaxValue.
            if (value is int i)
            {
                if (m_value < i) return -1;
                if (m_value > i) return 1;
                return 0;
            }

            throw new ArgumentException();
        }

        public int CompareTo(int value)
        {
            // NOTE: Cannot use return (_value - value) as this causes a wrap
            // around in cases where _value - value > MaxValue.
            if (m_value < value) return -1;
            if (m_value > value) return 1;
            return 0;
        }

        public bool Equals(int obj)
        {
            return m_value == obj;
        }
        public override int GetHashCode()
        {
            return m_value;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is int))
            {
                return false;
            }
            return m_value == ((int)obj).m_value;
        }

        public static int Parse(ReadOnlySpan<char> s)
        {
            int r;
            var st = System.Number.TryParseInt32(s, out r);
            if (st == System.Number.ParseStatus.OK) return r;
            if (st == System.Number.ParseStatus.Overflow) throw new OverflowException();
            throw new FormatException();
        }

        public static bool TryParse(ReadOnlySpan<char> s, out int result)
        {
            return System.Number.TryParseInt32(s, out result) == System.Number.ParseStatus.OK;
        }

        public static int Parse(String str)
        {
            if ((object)str == null) throw new ArgumentNullException("str");
            return Parse(str.AsSpan());
        }

        public static bool TryParse(String str, out int result)
        {
            if ((object)str == null) { result = 0; return false; }
            return TryParse(str.AsSpan(), out result);
        }

        public override string ToString()
        {
            return System.Number.FormatInt32(m_value, 0, null, null);
        }
        public string ToString(string? format)
        {
            return System.Number.FormatInt32(m_value, 0, format, null);
        }
        public string ToString(IFormatProvider? provider)
        {
            return System.Number.FormatInt32(m_value, 0, null, provider);
        }
    }
    public readonly struct UInt32
    {
        private readonly uint m_value;
        public const uint MaxValue = (uint)0xffffffff;
        public const uint MinValue = 0U;

        public bool Equals(uint obj)
        {
            return m_value == obj;
        }
        public override int GetHashCode()
        {
            return (int)m_value;
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is uint))
            {
                return false;
            }
            return m_value == ((uint)obj).m_value;
        }
        public int CompareTo(uint value)
        {
            // Need to use compare because subtraction will wrap
            // to positive for very large neg numbers, etc.
            if (m_value < value) return -1;
            if (m_value > value) return 1;
            return 0;
        }

        public static uint Log2(uint value) => (uint)BitOperations.Log2(value);

        public static uint Parse(ReadOnlySpan<char> s)
        {
            uint r;
            var st = System.Number.TryParseUInt32(s, out r);
            if (st == System.Number.ParseStatus.OK) return r;
            if (st == System.Number.ParseStatus.Overflow) throw new OverflowException();
            throw new FormatException();
        }

        public static bool TryParse(ReadOnlySpan<char> s, out uint result)
        {
            return System.Number.TryParseUInt32(s, out result) == System.Number.ParseStatus.OK;
        }

        public static uint Parse(String str)
        {
            if ((object)str == null) throw new ArgumentNullException("str");
            return Parse(str.AsSpan());
        }

        public static bool TryParse(String str, out uint result)
        {
            if ((object)str == null) { result = 0; return false; }
            return TryParse(str.AsSpan(), out result);
        }

        public override string ToString()
        {
            return System.Number.FormatUInt32(m_value, null, null);
        }
        public string ToString(string format)
        {
            return System.Number.FormatUInt32(m_value, format, null);
        }

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
        {
            return Number.TryFormatUInt32(m_value, format, provider, destination, out charsWritten);
        }

        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
        {
            return Number.TryFormatUInt32(m_value, format, provider, utf8Destination, out bytesWritten);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint CreateTruncating<TOther>(TOther value)
            where TOther : System.Numerics.INumberBase<TOther>
        {
            uint result;

            if (typeof(TOther) == typeof(uint))
            {
                result = (uint)(object)value;
            }
            else if (!TryConvertFromTruncating(value, out result) && !TOther.TryConvertToTruncating(value, out result))
            {
                throw new NotSupportedException();
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryConvertFromTruncating<TOther>(TOther value, out uint result)
            where TOther : System.Numerics.INumberBase<TOther>
        {
            if (typeof(TOther) == typeof(byte))
            {
                byte actualValue = (byte)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(char))
            {
                char actualValue = (char)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(decimal))
            {
                throw new NotSupportedException();
            }
            else if (typeof(TOther) == typeof(ushort))
            {
                ushort actualValue = (ushort)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(ulong))
            {
                ulong actualValue = (ulong)(object)value;
                result = (uint)actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(UInt128))
            {
                UInt128 actualValue = (UInt128)(object)value;
                result = (uint)actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(nuint))
            {
                nuint actualValue = (nuint)(object)value;
                result = (uint)actualValue;
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }
    }
    public readonly struct Int64
    {
        private readonly long m_value;
        public const long MaxValue = 0x7fffffffffffffffL;
        public const long MinValue = unchecked((long)0x8000000000000000L);

        public bool Equals(long obj)
        {
            return m_value == obj;
        }
        // The value of the lower 32 bits XORed with the uppper 32 bits.
        public override int GetHashCode()
        {
            return unchecked((int)((long)m_value)) ^ (int)(m_value >> 32);
        }
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (!(obj is long))
            {
                return false;
            }
            return m_value == ((long)obj).m_value;
        }
        public int CompareTo(long value)
        {
            // Need to use compare because subtraction will wrap
            // to positive for very large neg numbers, etc.
            if (m_value < value) return -1;
            if (m_value > value) return 1;
            return 0;
        }

        public static long Parse(ReadOnlySpan<char> s)
        {
            long r;
            var st = System.Number.TryParseInt64(s, out r);
            if (st == System.Number.ParseStatus.OK) return r;
            if (st == System.Number.ParseStatus.Overflow) throw new OverflowException();
            throw new FormatException();
        }

        public static bool TryParse(ReadOnlySpan<char> s, out long result)
        {
            return System.Number.TryParseInt64(s, out result) == System.Number.ParseStatus.OK;
        }

        public static long Parse(String str)
        {
            if ((object)str == null) throw new ArgumentNullException("str");
            return Parse(str.AsSpan());
        }

        public static bool TryParse(String str, out long result)
        {
            if ((object)str == null) { result = 0; return false; }
            return TryParse(str.AsSpan(), out result);
        }

        public override string ToString()
        {
            return System.Number.FormatInt64(m_value, null, null);
        }
        public string ToString(IFormatProvider? provider)
        {
            return System.Number.FormatInt64(m_value, null, provider);
        }
        public string ToString(string format)
        {
            return System.Number.FormatInt64(m_value, format, null);
        }
    }
    public readonly struct UInt64
    {
        private readonly ulong m_value;
        public const ulong MaxValue = (ulong)0xffffffffffffffffL;
        public const ulong MinValue = 0x0;

        public bool Equals(ulong obj)
        {
            return m_value == obj;
        }
        public int CompareTo(ulong value)
        {
            if (this < value)
            {
                return -1;
            }

            if (this > value)
            {
                return 1;
            }

            return 0;
        }
        // The value of the lower 32 bits XORed with the uppper 32 bits.
        public override int GetHashCode()
        {
            return ((int)m_value) ^ (int)(m_value >> 32);
        }

        public static ulong Log2(ulong value) => (ulong)System.Numerics.BitOperations.Log2(value);

        public static ulong Parse(ReadOnlySpan<char> s)
        {
            ulong r;
            var st = System.Number.TryParseUInt64(s, out r);
            if (st == System.Number.ParseStatus.OK) return r;
            if (st == System.Number.ParseStatus.Overflow) throw new OverflowException();
            throw new FormatException();
        }

        public static bool TryParse(ReadOnlySpan<char> s, out ulong result)
        {
            return System.Number.TryParseUInt64(s, out result) == System.Number.ParseStatus.OK;
        }

        public static ulong Parse(String str)
        {
            if ((object)str == null) throw new ArgumentNullException("str");
            return Parse(str.AsSpan());
        }

        public static bool TryParse(String str, out ulong result)
        {
            if ((object)str == null) { result = 0; return false; }
            return TryParse(str.AsSpan(), out result);
        }

        public override string ToString()
        {
            return System.Number.FormatUInt64(m_value, null, null);
        }
        public string ToString(IFormatProvider? provider)
        {
            return System.Number.FormatUInt64(m_value, null, provider);
        }
        public string ToString(string format)
        {
            return System.Number.FormatUInt64(m_value, format, null);
        }
    }
    public struct Single
    {
        private readonly float m_value;
        public const float MinValue = (float)-3.40282346638528859e+38;
        public const float MaxValue = (float)3.40282346638528859e+38;

        public const float Epsilon = (float)1.4e-45;
        public const float NegativeInfinity = (float)-1.0 / (float)0.0;
        public const float PositiveInfinity = (float)1.0 / (float)0.0;
        public const float NaN = (float)0.0 / (float)0.0;

        internal const float AdditiveIdentity = 0.0f;
        internal const float MultiplicativeIdentity = 1.0f;
        internal const float One = 1.0f;
        internal const float Zero = 0.0f;
        internal const float NegativeOne = -1.0f;
        public const float NegativeZero = -0.0f;


        internal const uint SignMask = 0x8000_0000;
        internal const int SignShift = 31;
        internal const byte ShiftedSignMask = (byte)(SignMask >> SignShift);

        internal const uint BiasedExponentMask = 0x7F80_0000;
        internal const int BiasedExponentShift = 23;
        internal const int BiasedExponentLength = 8;
        internal const byte ShiftedBiasedExponentMask = (byte)(BiasedExponentMask >> BiasedExponentShift);

        internal const uint TrailingSignificandMask = 0x007F_FFFF;

        internal const byte MinSign = 0;
        internal const byte MaxSign = 1;

        internal const byte MinBiasedExponent = 0x00;
        internal const byte MaxBiasedExponent = 0xFF;

        internal const byte ExponentBias = 127;

        internal const sbyte MinExponent = -126;
        internal const sbyte MaxExponent = +127;

        internal const uint MinTrailingSignificand = 0x0000_0000;
        internal const uint MaxTrailingSignificand = 0x007F_FFFF;

        internal const int TrailingSignificandLength = 23;
        internal const int SignificandLength = TrailingSignificandLength + 1;

        // Constants representing the private bit-representation for various default values

        internal const uint PositiveZeroBits = 0x0000_0000;
        internal const uint NegativeZeroBits = 0x8000_0000;

        internal const uint EpsilonBits = 0x0000_0001;

        internal const uint PositiveInfinityBits = 0x7F80_0000;
        internal const uint NegativeInfinityBits = 0xFF80_0000;

        internal const uint SmallestNormalBits = 0x0080_0000;

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return (obj is float other) && Equals(other);
        }
        public bool Equals(float obj)
        {
            if (obj == m_value)
            {
                return true;
            }
            return IsNaN(obj) && IsNaN(m_value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            uint bits = BitConverter.SingleToUInt32Bits(m_value);

            if (IsNaNOrZero(m_value))
            {
                // Ensure that all NaNs and both zeros have the same hash code
                bits &= PositiveInfinityBits;
            }

            return (int)bits;
        }

        public override string ToString()
        {
            return System.Number.FormatFloat(m_value, null, null);
        }
        public string ToString(IFormatProvider? provider)
        {
            return System.Number.FormatFloat(m_value, null, null);
        }
        public string ToString(string? format)
        {
            return System.Number.FormatFloat(m_value, format, null);
        }
        public static float Abs(float value) => MathF.Abs(value);
        public static float Sqrt(float x) => MathF.Sqrt(x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(float f)
        {
            uint bits = BitConverter.SingleToUInt32Bits(f);
            return (~bits & PositiveInfinityBits) != 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInfinity(float f)
        {
            uint bits = BitConverter.SingleToUInt32Bits(Abs(f));
            return bits == PositiveInfinityBits;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN(float f)
        {
            return f != f;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsNaNOrZero(float f)
        {
            uint bits = BitConverter.SingleToUInt32Bits(f);
            return ((bits - 1) & ~SignMask) >= PositiveInfinityBits;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegative(float f)
        {
            return BitConverter.SingleToInt32Bits(f) < 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegativeInfinity(float f)
        {
            return f == NegativeInfinity;
        }
    }
    public struct Double
    {
        private readonly double m_value;
        public const double MinValue = -1.7976931348623157E+308;
        public const double MaxValue = 1.7976931348623157E+308;

        public const double Epsilon = 4.9406564584124654E-324;
        public const double NegativeInfinity = (double)-1.0 / (double)(0.0);
        public const double PositiveInfinity = (double)1.0 / (double)(0.0);
        public const double NaN = (double)0.0 / (double)0.0;

        internal const ulong SignMask = 0x8000_0000_0000_0000;
        internal const int SignShift = 63;
        internal const byte ShiftedSignMask = (byte)(SignMask >> SignShift);

        internal const ulong BiasedExponentMask = 0x7FF0_0000_0000_0000;
        internal const int BiasedExponentShift = 52;
        internal const int BiasedExponentLength = 11;
        internal const ushort ShiftedBiasedExponentMask = (ushort)(BiasedExponentMask >> BiasedExponentShift);

        internal const ulong TrailingSignificandMask = 0x000F_FFFF_FFFF_FFFF;

        internal const byte MinSign = 0;
        internal const byte MaxSign = 1;

        internal const ushort MinBiasedExponent = 0x0000;
        internal const ushort MaxBiasedExponent = 0x07FF;

        internal const ushort ExponentBias = 1023;

        internal const short MinExponent = -1022;
        internal const short MaxExponent = +1023;

        internal const ulong MinTrailingSignificand = 0x0000_0000_0000_0000;
        internal const ulong MaxTrailingSignificand = 0x000F_FFFF_FFFF_FFFF;

        internal const int TrailingSignificandLength = 52;
        internal const int SignificandLength = TrailingSignificandLength + 1;


        internal const ulong PositiveZeroBits = 0x0000_0000_0000_0000;
        internal const ulong NegativeZeroBits = 0x8000_0000_0000_0000;

        internal const ulong EpsilonBits = 0x0000_0000_0000_0001;

        internal const ulong PositiveInfinityBits = 0x7FF0_0000_0000_0000;
        internal const ulong NegativeInfinityBits = 0xFFF0_0000_0000_0000;

        internal const ulong SmallestNormalBits = 0x0010_0000_0000_0000;


        public override string ToString()
        {
            return System.Number.FormatDouble(m_value, null, null);
        }
        public string ToString(string? format)
        {
            return System.Number.FormatDouble(m_value, format, null);
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return (obj is double other) && Equals(other);
        }
        public bool Equals(double obj)
        {
            if (obj == m_value)
            {
                return true;
            }
            return IsNaN(obj) && IsNaN(m_value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(m_value);

            if (IsNaNOrZero(m_value))
            {
                // Ensure that all NaNs and both zeros have the same hash code
                bits &= PositiveInfinityBits;
            }

            return unchecked((int)bits) ^ ((int)(bits >> 32));
        }

        public static double Abs(double value) => Math.Abs(value);
        public static double Sqrt(double x) => Math.Sqrt(x);
        public static double Truncate(double x) => Math.Truncate(x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(double d)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(d);
            return (~bits & PositiveInfinityBits) != 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInfinity(double d)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(Abs(d));
            return bits == PositiveInfinityBits;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN(double d) => d != d;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPositiveInfinity(double d)
        {
            return d == PositiveInfinity;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegativeInfinity(double d)
        {
            return d == NegativeInfinity;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegative(double d)
        {
            return BitConverter.DoubleToInt64Bits(d) < 0;
        }
        public static bool IsPositive(double value) => BitConverter.DoubleToInt64Bits(value) >= 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNormal(double d)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(Abs(d));
            return (bits - SmallestNormalBits) < (PositiveInfinityBits - SmallestNormalBits);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSubnormal(double d)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(Abs(d));
            return (bits - 1) < MaxTrailingSignificand;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsZero(double d)
        {
            return d == 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsNaNOrZero(double d)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(d);
            return ((bits - 1) & ~SignMask) >= PositiveInfinityBits;
        }
        public static bool IsInteger(double value) => IsFinite(value) && (value == Truncate(value));
        public static bool IsEvenInteger(double value) => IsInteger(value) && (Abs(value % 2) == 0);
        public static bool IsOddInteger(double value) => IsInteger(value) && (Abs(value % 2) == 1);

    }
    public struct Decimal
    {
        private readonly decimal m_value;
        public const decimal MaxValue = 79228162514264337593543950335m;
        public const decimal MinValue = -79228162514264337593543950335m;

        public override string ToString()
        {
            return System.Number.FormatDouble((double)m_value, null, null);
        }
    }

    public readonly struct Half
    {
        internal const ushort SignMask = 0x8000;
        internal const int SignShift = 15;
        internal const byte ShiftedSignMask = SignMask >> SignShift;

        internal const ushort BiasedExponentMask = 0x7C00;
        internal const int BiasedExponentShift = 10;
        internal const int BiasedExponentLength = 5;
        internal const byte ShiftedBiasedExponentMask = BiasedExponentMask >> BiasedExponentShift;

        internal const ushort TrailingSignificandMask = 0x03FF;

        internal const byte MinSign = 0;
        internal const byte MaxSign = 1;

        internal const byte MinBiasedExponent = 0x00;
        internal const byte MaxBiasedExponent = 0x1F;

        internal const byte ExponentBias = 15;

        internal const sbyte MinExponent = -14;
        internal const sbyte MaxExponent = +15;

        internal const ushort MinTrailingSignificand = 0x0000;
        internal const ushort MaxTrailingSignificand = 0x03FF;

        internal const int TrailingSignificandLength = 10;
        internal const int SignificandLength = TrailingSignificandLength + 1;

        // Constants representing the private bit-representation for various default values

        private const ushort PositiveZeroBits = 0x0000;
        private const ushort NegativeZeroBits = 0x8000;

        private const ushort EpsilonBits = 0x0001;

        private const ushort PositiveInfinityBits = 0x7C00;
        private const ushort NegativeInfinityBits = 0xFC00;

        private const ushort PositiveQNaNBits = 0x7E00;
        private const ushort NegativeQNaNBits = 0xFE00;

        private const ushort MinValueBits = 0xFBFF;
        private const ushort MaxValueBits = 0x7BFF;

        private const ushort PositiveOneBits = 0x3C00;
        private const ushort NegativeOneBits = 0xBC00;

        private const ushort SmallestNormalBits = 0x0400;

        private const ushort EBits = 0x4170;
        private const ushort PiBits = 0x4248;
        private const ushort TauBits = 0x4648;

        // Well-defined and commonly used values

        public static Half Epsilon => new Half(EpsilonBits);                        //  5.9604645E-08

        public static Half PositiveInfinity => new Half(PositiveInfinityBits);      //  1.0 / 0.0;

        public static Half NegativeInfinity => new Half(NegativeInfinityBits);      // -1.0 / 0.0

        public static Half NaN => new Half(NegativeQNaNBits);                       //  0.0 / 0.0

        public static Half MinValue => new Half(MinValueBits);                      // -65504

        public static Half MaxValue => new Half(MaxValueBits);                      //  65504

        internal readonly ushort _value;

        internal Half(ushort value)
        {
            _value = value;
        }

        private Half(bool sign, ushort exp, ushort sig) => _value = (ushort)(((sign ? 1 : 0) << SignShift) + (exp << BiasedExponentShift) + sig);
    }
    public readonly struct Int128
    {
        private readonly ulong _lower;
        private readonly ulong _upper;

        public Int128(ulong upper, ulong lower)
        {
            _lower = lower;
            _upper = upper;
        }

        internal ulong Lower => _lower;

        internal ulong Upper => _upper;

        public int CompareTo(Int128 value)
        {
            if (this < value)
            {
                return -1;
            }
            else if (this > value)
            {
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return (obj is Int128 other) && Equals(other);
        }

        public bool Equals(Int128 other)
        {
            return this == other;
        }

        public static bool operator ==(Int128 left, Int128 right) => (left._lower == right._lower) && (left._upper == right._upper);

        public static bool operator !=(Int128 left, Int128 right) => (left._lower != right._lower) || (left._upper != right._upper);

        public static Int128 operator &(Int128 left, Int128 right) => new Int128(left._upper & right._upper, left._lower & right._lower);

        public static Int128 operator |(Int128 left, Int128 right) => new Int128(left._upper | right._upper, left._lower | right._lower);

        public static Int128 operator ^(Int128 left, Int128 right) => new Int128(left._upper ^ right._upper, left._lower ^ right._lower);

        public static Int128 operator ~(Int128 value) => new Int128(~value._upper, ~value._lower);

        public static bool operator <(Int128 left, Int128 right)
        {
            // If left and right have different signs: Signed comparison of _upper gives result since it is stored as two's complement
            // If signs are equal and left._upper < right._upper: left < right for negative and positive values,
            //                                                    since _upper is upper 64 bits in two's complement.
            // If signs are equal and left._upper > right._upper: left > right for negative and positive values,
            //                                                    since _upper is upper 64 bits in two's complement.
            // If left._upper == right._upper: unsigned comparison of _lower gives the result for both negative and positive values since
            //                                 lower values are lower 64 bits in two's complement.
            return ((long)left._upper < (long)right._upper)
                || ((left._upper == right._upper) && (left._lower < right._lower));
        }

        public static bool operator <=(Int128 left, Int128 right)
        {
            return ((long)left._upper < (long)right._upper)
                || ((left._upper == right._upper) && (left._lower <= right._lower));
        }

        public static bool operator >(Int128 left, Int128 right)
        {
            return ((long)left._upper > (long)right._upper)
                || ((left._upper == right._upper) && (left._lower > right._lower));
        }

        public static bool operator >=(Int128 left, Int128 right)
        {
            return ((long)left._upper > (long)right._upper)
                || ((left._upper == right._upper) && (left._lower >= right._lower));
        }
    }
    public readonly struct UInt128
    {
        internal const int Size = 16;

        private readonly ulong _lower;
        private readonly ulong _upper;

        public UInt128(ulong upper, ulong lower)
        {
            _lower = lower;
            _upper = upper;
        }

        internal ulong Lower => _lower;

        internal ulong Upper => _upper;

        public int CompareTo(UInt128 value)
        {
            if (this < value)
            {
                return -1;
            }
            else if (this > value)
            {
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return (obj is UInt128 other) && Equals(other);
        }

        public bool Equals(UInt128 other)
        {
            return this == other;
        }

        public static explicit operator char(UInt128 value) => (char)value._lower;

        public static bool operator ==(UInt128 left, UInt128 right) => (left._lower == right._lower) && (left._upper == right._upper);

        public static bool operator !=(UInt128 left, UInt128 right) => (left._lower != right._lower) || (left._upper != right._upper);

        public static UInt128 operator &(UInt128 left, UInt128 right) => new UInt128(left._upper & right._upper, left._lower & right._lower);

        public static UInt128 operator |(UInt128 left, UInt128 right) => new UInt128(left._upper | right._upper, left._lower | right._lower);

        public static UInt128 operator ^(UInt128 left, UInt128 right) => new UInt128(left._upper ^ right._upper, left._lower ^ right._lower);

        public static UInt128 operator ~(UInt128 value) => new UInt128(~value._upper, ~value._lower);

        public static bool operator <(UInt128 left, UInt128 right)
        {
            return (left._upper < right._upper)
                || (left._upper == right._upper) && (left._lower < right._lower);
        }

        public static bool operator <=(UInt128 left, UInt128 right)
        {
            return (left._upper < right._upper)
                || (left._upper == right._upper) && (left._lower <= right._lower);
        }

        public static bool operator >(UInt128 left, UInt128 right)
        {
            return (left._upper > right._upper)
                || (left._upper == right._upper) && (left._lower > right._lower);
        }

        public static bool operator >=(UInt128 left, UInt128 right)
        {
            return (left._upper > right._upper)
                || (left._upper == right._upper) && (left._lower >= right._lower);
        }
    }
    internal static class FormattingHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountDigits(ulong value)
        {
            // Map the log2(value) to a power of 10.
            ReadOnlySpan<byte> log2ToPow10 =
            [
                1,  1,  1,  2,  2,  2,  3,  3,  3,  4,  4,  4,  4,  5,  5,  5,
                6,  6,  6,  7,  7,  7,  7,  8,  8,  8,  9,  9,  9,  10, 10, 10,
                10, 11, 11, 11, 12, 12, 12, 13, 13, 13, 13, 14, 14, 14, 15, 15,
                15, 16, 16, 16, 16, 17, 17, 17, 18, 18, 18, 19, 19, 19, 19, 20
            ];

            nint elementOffset = log2ToPow10[(int)ulong.Log2(value)];

            // Read the associated power of 10.
            ReadOnlySpan<ulong> powersOf10 =
            [
                0, // unused entry to avoid needing to subtract
                0,
                10,
                100,
                1000,
                10000,
                100000,
                1000000,
                10000000,
                100000000,
                1000000000,
                10000000000,
                100000000000,
                1000000000000,
                10000000000000,
                100000000000000,
                1000000000000000,
                10000000000000000,
                100000000000000000,
                1000000000000000000,
                10000000000000000000,
            ];

            ulong powerOf10 = Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference<ulong>(powersOf10), elementOffset);

            // Return the number of digits based on the power of 10, shifted by 1
            // if it falls below the threshold.
            int index = (int)elementOffset;
            return index - (value < powerOf10 ? 1 : 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountDigits(uint value)
        {
            ReadOnlySpan<long> table =
            [
                4294967296,
                8589934582,
                8589934582,
                8589934582,
                12884901788,
                12884901788,
                12884901788,
                17179868184,
                17179868184,
                17179868184,
                21474826480,
                21474826480,
                21474826480,
                21474826480,
                25769703776,
                25769703776,
                25769703776,
                30063771072,
                30063771072,
                30063771072,
                34349738368,
                34349738368,
                34349738368,
                34349738368,
                38554705664,
                38554705664,
                38554705664,
                41949672960,
                41949672960,
                41949672960,
                42949672960,
                42949672960,
            ];

            long tableValue = table[(int)uint.Log2(value)];
            return (int)((value + tableValue) >> 32);
        }

        // Counts the number of trailing '0' digits in a decimal number.
        // e.g., value =      0 => retVal = 0, valueWithoutTrailingZeros = 0
        //       value =   1234 => retVal = 0, valueWithoutTrailingZeros = 1234
        //       value = 320900 => retVal = 2, valueWithoutTrailingZeros = 3209
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountDecimalTrailingZeros(uint value, out uint valueWithoutTrailingZeros)
        {
            int zeroCount = 0;

            if (value != 0)
            {
                while (true)
                {
                    uint temp = value / 10;
                    if (value != (temp * 10))
                    {
                        break;
                    }

                    value = temp;
                    zeroCount++;
                }
            }

            valueWithoutTrailingZeros = value;
            return zeroCount;
        }
    }
    internal static unsafe class Number
    {
        // We need 1 additional byte, per length, for the terminating null
        internal const int DecimalNumberBufferLength = 29 + 1 + 1;  // 29 for the longest input + 1 for rounding
        internal const int DoubleNumberBufferLength = 767 + 1 + 1;  // 767 for the longest input + 1 for rounding: 4.9406564584124654E-324
        internal const int Int32NumberBufferLength = 10 + 1;    // 10 for the longest input: 2,147,483,647
        internal const int Int64NumberBufferLength = 19 + 1;    // 19 for the longest input: 9,223,372,036,854,775,807
        internal const int Int128NumberBufferLength = 39 + 1;    // 39 for the longest input: 170,141,183,460,469,231,731,687,303,715,884,105,727
        internal const int SingleNumberBufferLength = 112 + 1 + 1;  // 112 for the longest input + 1 for rounding: 1.40129846E-45
        internal const int HalfNumberBufferLength = 21 + 1 + 1; // 21 for the longest input + 1 for rounding: 0.000122010707855224609375
        internal const int UInt32NumberBufferLength = 10 + 1;   // 10 for the longest input: 4,294,967,295
        internal const int UInt64NumberBufferLength = 20 + 1;   // 20 for the longest input: 18,446,744,073,709,551,615
        internal const int UInt128NumberBufferLength = 39 + 1; // 39 for the longest input: 340,282,366,920,938,463,463,374,607,431,768,211,455
        internal const int Decimal32NumberBufferLength = 7 + 1 + 1; // 7 for the longest input + 1 for rounding
        internal const int Decimal64NumberBufferLength = 16 + 1 + 1; // 16 for the longest input + 1 for rounding
        internal const int Decimal128NumberBufferLength = 34 + 1 + 1; // 34 for the longest input + 1 for rounding

        internal unsafe ref struct NumberBuffer
        {
            public int DigitsCount;
            public int Scale;
            public bool IsNegative;
            public bool HasNonZeroTail;
            public NumberBufferKind Kind;
            public Span<byte> Digits;
            /// <safety>Converts the ref to Digits into a pointer value via Unsafe.AsPointer and returns it without dereferencing; 
            /// the result is not GC-tracked, so any use must be in an unsafe context that establishes Digits still refers to unmovable memory.</safety>
            public readonly byte* DigitsPtr => 
                (byte*)Unsafe.AsPointer(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(Digits)); // safe since constructor expects Digits to refer to unmovable memory

            public NumberBuffer(NumberBufferKind kind, byte* digits, int digitsLength) : this(kind, new Span<byte>(digits, digitsLength))
            {

            }

            public NumberBuffer(NumberBufferKind kind, Span<byte> digits)
            {
                DigitsCount = 0;
                Scale = 0;
                IsNegative = false;
                HasNonZeroTail = false;
                Kind = kind;
                Digits = digits;
                Digits[0] = (byte)'\0';
            }
        }
        internal enum NumberBufferKind : byte
        {
            Unknown = 0,
            Integer = 1,
            Decimal = 2,
            FloatingPoint = 3,

            /// <summary>
            /// An IEEE 754 decimal interchange format. Unlike <see cref="NumberBufferKind.FloatingPoint"/> the buffer
            /// holds the exact coefficient rather than a pre-rounded shortest representation, so formatting must round
            /// it; unlike <see cref="NumberBufferKind.Decimal"/> that rounding is ties-to-even and a signed zero must
            /// survive it.
            /// </summary>
            DecimalIeee754 = 4,
        }

        internal const int DecimalPrecision = 29;
        private const int SmallNumberCacheLength = 300;

        private static ReadOnlySpan<byte> TwoDigitsCharsAsBytes =>
            System.Runtime.InteropServices.MemoryMarshal.AsBytes<char>("00010203040506070809" +
                                        "10111213141516171819" +
                                        "20212223242526272829" +
                                        "30313233343536373839" +
                                        "40414243444546474849" +
                                        "50515253545556575859" +
                                        "60616263646566676869" +
                                        "70717273747576777879" +
                                        "80818283848586878889" +
                                        "90919293949596979899");
        private static ReadOnlySpan<byte> TwoDigitsBytes =>
                                        "00010203040506070809"u8 +
                                        "10111213141516171819"u8 +
                                        "20212223242526272829"u8 +
                                        "30313233343536373839"u8 +
                                        "40414243444546474849"u8 +
                                        "50515253545556575859"u8 +
                                        "60616263646566676869"u8 +
                                        "70717273747576777879"u8 +
                                        "80818283848586878889"u8 +
                                        "90919293949596979899"u8;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref byte GetTwoDigitsBytesRef(bool useChars) =>
            ref System.Runtime.InteropServices.MemoryMarshal.GetReference(useChars ? TwoDigitsCharsAsBytes : TwoDigitsBytes);


        internal enum ParseStatus : byte
        {
            OK = 0,
            Format = 1,
            Overflow = 2,
        }
        internal static string CharToString(char c)
        {
            string s = String.FastAllocateString(1);
            ref char dst = ref s.GetRawStringData();
            dst = c;
            return s;
        }
        private static bool TryParseHexFormat(string? format, out bool upperCase, out int precision)
        {
            upperCase = true;
            precision = 0;

            if (string.IsNullOrEmpty(format))
                return false;

            char specifier = format[0];
            if (specifier != 'X' && specifier != 'x')
                return false;

            upperCase = specifier == 'X';

            for (int i = 1; i < format.Length; i++)
            {
                char c = format[i];
                if (c < '0' || c > '9')
                    return false;

                int digit = c - '0';
                if (precision > (0x7fffffff - digit) / 10)
                    throw new FormatException("Format specifier precision is too large.");

                precision = precision * 10 + digit;
            }

            return true;
        }
        private static int CountHexDigits64(ulong value)
        {
            int digits = 1;
            while ((value >>= 4) != 0ul)
                digits++;

            return digits;
        }
        public static string FormatInt32(int value, int hexMask, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return Int32ToString(value);
            }
            if (TryParseHexFormat(format, out bool upperCase, out int precision))
            {
                uint hexValue = hexMask == 0 ? (uint)value : ((uint)value & (uint)hexMask);
                return UInt32ToHexString(hexValue, precision, upperCase);
            }
            throw new NotSupportedException($"format {format} not supported");
        }
        internal static unsafe string Int32ToString(int value)
        {
            if (value == unchecked((int)0x80000000))
                return "-2147483648";
            char* buffer = stackalloc char[12]; // sign + 10 digits + terminator
            char* p = buffer + 12;

            bool neg = value < 0;
            uint v = (uint)(neg ? -value : value);

            do
            {
                uint digit = v % 10u;
                v /= 10u;
                *--p = (char)('0' + digit);
            } while (v != 0u);

            if (neg) *--p = '-';

            int len = (int)((buffer + 12) - p);
            string s = String.FastAllocateString(len);
            ref char dst = ref s.GetRawStringData();

            for (int i = 0; i < len; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = p[i];

            return s;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // expose to caller's likely-const format to trim away slow path
        public static bool TryFormatUInt32<TChar>(
            uint value, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten) 
            where TChar : unmanaged, IUtfChar<TChar>
        {
            // Fast path for default format
            if (format.Length == 0)
            {
                return TryUInt32ToDecStr(value, destination, out charsWritten);
            }

            //return TryFormatUInt32Slow(value, format, provider, destination, out charsWritten);
            throw new NotSupportedException();
        }
        internal static unsafe bool TryUInt32ToDecStr<TChar>(uint value, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            int bufferLength = FormattingHelpers.CountDigits(value);
            if (bufferLength <= destination.Length)
            {
                charsWritten = bufferLength;
                fixed (TChar* buffer = &System.Runtime.InteropServices.MemoryMarshal.GetReference(destination))
                {
                    TChar* p = UInt32ToDecChars(buffer + bufferLength, value);
                }
                return true;
            }

            charsWritten = 0;
            return false;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe TChar* UInt32ToDecChars<TChar>(TChar* bufferEnd, uint value) where TChar : unmanaged, IUtfChar<TChar>
        {
            if (value >= 10)
            {
                // Handle all values >= 100 two-digits at a time so as to avoid expensive integer division operations.
                while (value >= 100)
                {
                    bufferEnd -= 2;
                    (value, uint remainder) = Math.DivRem(value, 100);
                    WriteTwoDigits(remainder, bufferEnd);
                }

                // If there are two digits remaining, store them.
                if (value >= 10)
                {
                    bufferEnd -= 2;
                    WriteTwoDigits(value, bufferEnd);
                    return bufferEnd;
                }
            }
            // Otherwise, store the single digit remaining.
            *(--bufferEnd) = TChar.CastFrom(value + '0');
            return bufferEnd;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void WriteTwoDigits<TChar>(uint value, TChar* ptr) where TChar : unmanaged, IUtfChar<TChar>
        {
            Unsafe.CopyBlockUnaligned(
                ref *(byte*)ptr,
                ref Unsafe.Add(ref GetTwoDigitsBytesRef(typeof(TChar) == typeof(char)), (uint)sizeof(TChar) * 2 * value),
                (uint)sizeof(TChar) * 2);
        }
        public static string FormatUInt32(uint value, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return UInt32ToString(value);
            }
            if (TryParseHexFormat(format, out bool upperCase, out int precision))
            {
                return UInt32ToHexString(value, precision, upperCase);
            }
            throw new NotSupportedException($"format {format} not supported");
        }
        internal static unsafe string UInt32ToString(uint value)
        {
            char* buffer = stackalloc char[11]; // 10 digits + terminator
            char* p = buffer + 11;

            uint v = value;
            do
            {
                uint digit = v % 10u;
                v /= 10u;
                *--p = (char)('0' + digit);
            } while (v != 0u);

            int len = (int)((buffer + 11) - p);
            string s = String.FastAllocateString(len);
            ref char dst = ref s.GetRawStringData();

            for (int i = 0; i < len; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = p[i];

            return s;
        }
        internal static string FormatInt64(long value, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return Int64ToString(value);
            }
            if (TryParseHexFormat(format, out bool upperCase, out int precision))
            {
                return UInt64ToHexString((ulong)value, precision, upperCase);
            }
            throw new NotSupportedException($"format {format} not supported");
        }
        private static unsafe string Int64ToString(long value)
        {
            if (value == unchecked((long)0x8000000000000000)) // long.MinValue
                return "-9223372036854775808";

            char* buffer = stackalloc char[21]; // sign + 19 digits + terminator
            char* p = buffer + 21;

            bool neg = value < 0;
            ulong v = (ulong)(neg ? -value : value);

            do
            {
                ulong digit = v % 10ul;
                v /= 10ul;
                *--p = (char)('0' + digit);
            } while (v != 0ul);

            if (neg) *--p = '-';

            int len = (int)((buffer + 21) - p);
            string s = String.FastAllocateString(len);
            ref char dst = ref s.GetRawStringData();

            for (int i = 0; i < len; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = p[i];

            return s;
        }
        public static string FormatUInt64(ulong value, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return UInt64ToString(value);
            }
            if (TryParseHexFormat(format, out bool upperCase, out int precision))
            {
                return UInt64ToHexString(value, precision, upperCase);
            }
            throw new NotSupportedException($"format {format} not supported");
        }

        internal static unsafe string UInt64ToString(ulong value)
        {
            char* buffer = stackalloc char[21]; // 20 digits + terminator
            char* p = buffer + 21;

            do
            {
                ulong digit = value % 10ul;
                value /= 10ul;
                *--p = (char)('0' + digit);
            } while (value != 0ul);

            int len = (int)((buffer + 21) - p);
            string s = String.FastAllocateString(len);
            ref char dst = ref s.GetRawStringData();

            for (int i = 0; i < len; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = p[i];

            return s;
        }

        private static string UInt64ToHexString(ulong value, int precision, bool upperCase)
        {
            int digitCount = CountHexDigits64(value);
            int len = precision > digitCount ? precision : digitCount;

            string s = String.FastAllocateString(len);
            ref char dst = ref s.GetRawStringData();

            int i = len;
            char alphaBase = upperCase ? 'A' : 'a';
            ulong v = value;

            do
            {
                int digit = (int)(v & 0xFul);
                v >>= 4;

                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, --i) =
                    (char)(digit < 10 ? '0' + digit : alphaBase + (digit - 10));
            } while (v != 0ul);

            while (i > 0)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, --i) = '0';

            return s;
        }
        private static string UInt32ToHexString(uint value, int precision, bool upperCase)
        {
            return UInt64ToHexString((ulong)value, precision, upperCase);
        }
        internal const int FloatFormatBufferCharCount = 32;
        internal const int DoubleFormatBufferCharCount = 32;
        private static unsafe string StringFromCharBuffer(char* source, int length)
        {
            string s = String.FastAllocateString(length);
            ref char dst = ref s.GetRawStringData();
            for (int i = 0; i < length; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = source[i];
            return s;
        }

        private static unsafe int FormatUnsignedIntegerToBuffer(ulong value, bool negative, char* destination, int destinationLength)
        {
            char* buffer = stackalloc char[20];
            char* p = buffer + 20;

            do
            {
                ulong digit = value % 10UL;
                value /= 10UL;
                *--p = (char)('0' + digit);
            } while (value != 0UL);

            int digitCount = (int)((buffer + 20) - p);
            int length = digitCount + (negative ? 1 : 0);

            int pos = 0;
            if (negative)
                destination[pos++] = '-';

            for (int i = 0; i < digitCount; i++)
                destination[pos++] = p[i];

            return length;
        }
        internal static unsafe string FormatFloat(float value, string? format, System.Globalization.NumberFormatInfo? info)
        {
            char* buffer = stackalloc char[FloatFormatBufferCharCount];
            int length = FormatFloatToBuffer(value, format, info, buffer, FloatFormatBufferCharCount);
            return StringFromCharBuffer(buffer, length);
        }
        internal static unsafe int FormatFloatToBuffer(float value, string? format, System.Globalization.NumberFormatInfo? info, char* destination, int destinationLength)
        {
            uint bits = BitConverter.SingleToUInt32Bits(value);
            bool negative = (bits & 0x8000_0000U) != 0;
            uint absBits = bits & 0x7FFF_FFFFU;

            if ((absBits & 0x7F80_0000U) == 0x7F80_0000U)
            {
                if ((absBits & 0x007F_FFFFU) != 0)
                {
                    destination[0] = 'N';
                    destination[1] = 'a';
                    destination[2] = 'N';
                    return 3;
                }

                if (negative) //-Infinity
                {
                    destination[0] = '-';
                    destination[1] = 'I';
                    destination[2] = 'n';
                    destination[3] = 'f';
                    destination[4] = 'i';
                    destination[5] = 'n';
                    destination[6] = 'i';
                    destination[7] = 't';
                    destination[8] = 'y';
                    return 9;
                }
                else //Infinity
                {
                    destination[0] = 'I';
                    destination[1] = 'n';
                    destination[2] = 'f';
                    destination[3] = 'i';
                    destination[4] = 'n';
                    destination[5] = 'i';
                    destination[6] = 't';
                    destination[7] = 'y';
                    return 8;
                }
            }

            if (absBits == 0)
            {
                destination[0] = '0';
                return 1;
            }

            if (value > -2147483648.0f && value < 2147483648.0f)
            {
                int integerValue = (int)value;
                if ((float)integerValue == value)
                {
                    if (value == unchecked((int)0x80000000))
                    {
                        destination[0] = '-';
                        destination[1] = '2';
                        destination[2] = '1';
                        destination[3] = '4';
                        destination[4] = '7';
                        destination[5] = '4';
                        destination[6] = '8';
                        destination[7] = '3';
                        destination[8] = '6';
                        destination[9] = '4';
                        destination[10] = '8';
                        return 11;
                    }
                    uint magnitude = (uint)(value < 0 ? -value : value);
                    return FormatUnsignedIntegerToBuffer(magnitude, value < 0, destination, destinationLength);
                }
            }

            return FormatDoubleToBuffer((double)value, format, info, destination, destinationLength);
        }

        private const int DoubleFormatBigUIntMaxWords = 48;

        private unsafe struct BigUIntScratch
        {
            public uint* Words;
            public int Length;
            public int Capacity;
        }
        internal static unsafe string FormatDouble(double value, string? format, System.Globalization.NumberFormatInfo? info)
        {
            char* buffer = stackalloc char[DoubleFormatBufferCharCount];
            int length = FormatDoubleToBuffer(value, format, info, buffer, DoubleFormatBufferCharCount);
            return StringFromCharBuffer(buffer, length);
        }
        internal static unsafe int FormatDoubleToBuffer(double value, string? format, System.Globalization.NumberFormatInfo? info, char* destination, int destinationLength)
        {
            const ulong SignMask = 0x8000_0000_0000_0000UL;
            const ulong MantissaMask = 0x000F_FFFF_FFFF_FFFFUL;
            const ulong ExponentMask = 0x7FF0_0000_0000_0000UL;
            const int MantissaBits = 52;
            const int ExponentBias = 1023;

            ulong bits = BitConverter.DoubleToUInt64Bits(value);
            bool negative = (bits & SignMask) != 0;
            ulong absBits = bits & ~SignMask;

            if ((absBits & ExponentMask) == ExponentMask)
            {
                if ((absBits & MantissaMask) != 0)
                {
                    destination[0] = 'N';
                    destination[1] = 'a';
                    destination[2] = 'N';
                    return 3;
                }

                if (negative) //-Infinity
                {
                    destination[0] = '-';
                    destination[1] = 'I';
                    destination[2] = 'n';
                    destination[3] = 'f';
                    destination[4] = 'i';
                    destination[5] = 'n';
                    destination[6] = 'i';
                    destination[7] = 't';
                    destination[8] = 'y';
                    return 9;
                }
                else //Infinity
                {
                    destination[0] = 'I';
                    destination[1] = 'n';
                    destination[2] = 'f';
                    destination[3] = 'i';
                    destination[4] = 'n';
                    destination[5] = 'i';
                    destination[6] = 't';
                    destination[7] = 'y';
                    return 8;
                }
            }

            if (absBits == 0)
            {
                destination[0] = '0';
                return 1;
            }

            if (value > -9223372036854775808.0 && value < 9223372036854775808.0)
            {
                long integerValue = (long)value;
                if ((double)integerValue == value)
                {
                    if (value == unchecked((long)0x8000000000000000))
                    {
                        destination[0] = '-';
                        destination[1] = '9';
                        destination[2] = '2';
                        destination[3] = '2';
                        destination[4] = '3';
                        destination[5] = '3';
                        destination[6] = '7';
                        destination[7] = '2';
                        destination[8] = '0';
                        destination[9] = '3';
                        destination[10] = '6';
                        destination[11] = '8';
                        destination[12] = '5';
                        destination[13] = '4';
                        destination[14] = '7';
                        destination[15] = '7';
                        destination[16] = '5';
                        destination[17] = '8';
                        destination[18] = '0';
                        destination[19] = '8';
                        return 20;
                    }
                    ulong magnitude = (ulong)(value < 0 ? -value : value);
                    return FormatUnsignedIntegerToBuffer(magnitude, value < 0, destination, destinationLength);
                }
            }

            ulong ieeeMantissa = bits & MantissaMask;
            int ieeeExponent = (int)((bits >> MantissaBits) & 0x7FFUL);

            ulong mantissa;
            int binaryExponent;
            if (ieeeExponent == 0)
            {
                mantissa = ieeeMantissa;
                binaryExponent = -1074;
            }
            else
            {
                mantissa = (1UL << MantissaBits) | ieeeMantissa;
                binaryExponent = ieeeExponent - ExponentBias - MantissaBits;
            }

            int decimalExponent = ComputeDecimalExponent(mantissa, binaryExponent);
            int decimalScale = decimalExponent - 16;

            ulong digits = ComputeRoundedScaledDigits(mantissa, binaryExponent, decimalScale);
            if (digits >= 100000000000000000UL)
            {
                digits /= 10UL;
                decimalScale++;
            }

            ulong boundaryValue = mantissa << 2;
            ulong upperBoundary = boundaryValue + 2UL;
            int lowerBoundaryShift = (ieeeMantissa != 0 || ieeeExponent <= 1) ? 1 : 0;
            ulong lowerBoundary = boundaryValue - 1UL - (ulong)lowerBoundaryShift;
            int boundaryExponent = binaryExponent - 2;
            bool acceptBoundary = (mantissa & 1UL) == 0;

            while (digits >= 10UL)
            {
                ulong q = digits / 10UL;
                int nextScale = decimalScale + 1;

                bool lowerCandidate = IsDecimalInRoundInterval(q, nextScale, lowerBoundary, upperBoundary, boundaryExponent, acceptBoundary);
                bool upperCandidate = IsDecimalInRoundInterval(q + 1UL, nextScale, lowerBoundary, upperBoundary, boundaryExponent, acceptBoundary);

                if (!lowerCandidate && !upperCandidate)
                    break;

                if (lowerCandidate && upperCandidate)
                {
                    ulong midpointDigits = checked(q * 2UL + 1UL);
                    int midpointCompare = -CompareDecimalToBinary(midpointDigits, nextScale, boundaryValue, boundaryExponent + 1);

                    if (midpointCompare < 0)
                    {
                        digits = q;
                    }
                    else if (midpointCompare > 0)
                    {
                        digits = q + 1UL;
                    }
                    else
                    {
                        digits = ((q & 1UL) == 0) ? q : q + 1UL;
                    }
                }
                else
                {
                    digits = lowerCandidate ? q : q + 1UL;
                }

                decimalScale = nextScale;
            }

            return FormatShortestDoubleToBuffer(negative, digits, decimalScale, destination, destinationLength);
        }
        private static int ComputeDecimalExponent(ulong mantissa, int binaryExponent)
        {
            int binaryFloorExponent = binaryExponent + BitLength(mantissa) - 1;
            int decimalExponent = FloorLog10Pow2(binaryFloorExponent);

            while (ComparePositiveBinaryFloatToPowerOf10(mantissa, binaryExponent, decimalExponent + 1) >= 0)
                decimalExponent++;

            while (ComparePositiveBinaryFloatToPowerOf10(mantissa, binaryExponent, decimalExponent) < 0)
                decimalExponent--;

            return decimalExponent;
        }
        private static int FloorLog10Pow2(int exponent)
        {
            return (int)(((long)exponent * 78913L) >> 18);
        }
        private static int BitLength(ulong value)
        {
            int length = 0;
            while (value != 0)
            {
                length++;
                value >>= 1;
            }
            return length;
        }
        private static unsafe ulong ComputeRoundedScaledDigits(ulong mantissa, int binaryExponent, int decimalScale)
        {
            uint* numeratorStorage = stackalloc uint[DoubleFormatBigUIntMaxWords * 4];

            BigUIntScratch numerator = CreateBigUIntScratch(numeratorStorage);
            BigUIntScratch denominator = CreateBigUIntScratch(numeratorStorage + DoubleFormatBigUIntMaxWords);
            BigUIntScratch remainder = CreateBigUIntScratch(numeratorStorage + DoubleFormatBigUIntMaxWords * 2);
            BigUIntScratch temp = CreateBigUIntScratch(numeratorStorage + DoubleFormatBigUIntMaxWords * 3);

            BigUIntSetUInt64(ref numerator, mantissa);
            if (binaryExponent >= 0)
            {
                BigUIntShiftLeft(ref numerator, binaryExponent);
            }
            else if (decimalScale < 0)
            {
                BigUIntMultiplyPow10(ref numerator, -decimalScale);
                int denominatorShift = -binaryExponent;
                ulong pow2Result = BigUIntDivRemPow2ToUInt64(ref numerator, denominatorShift, ref remainder);
                int pow2Cmp = BigUIntCompareTwiceToPowerOfTwo(ref remainder, denominatorShift);
                if (pow2Cmp > 0 || (pow2Cmp == 0 && (pow2Result & 1UL) != 0))
                    pow2Result++;
                return pow2Result;
            }
            BigUIntSetUInt64(ref denominator, 1UL);

            if (binaryExponent < 0)
                BigUIntShiftLeft(ref denominator, -binaryExponent);

            if (decimalScale >= 0)
                BigUIntMultiplyPow10(ref denominator, decimalScale);
            else
                BigUIntMultiplyPow10(ref numerator, -decimalScale);

            ulong result = BigUIntDivRemToUInt64(ref numerator, ref denominator, ref remainder, ref temp);
            BigUIntShiftLeft(ref remainder, 1);

            int cmp = BigUIntCompare(ref remainder, ref denominator);
            if (cmp > 0 || (cmp == 0 && (result & 1UL) != 0))
                result++;

            return result;
        }
        private static bool IsDecimalInRoundInterval(
            ulong decimalDigits,
            int decimalScale,
            ulong lowerBoundary,
            ulong upperBoundary,
            int binaryBoundaryExponent,
            bool acceptBoundary)
        {
            int lowerCmp = CompareDecimalToBinary(decimalDigits, decimalScale, lowerBoundary, binaryBoundaryExponent);
            if (acceptBoundary)
            {
                if (lowerCmp < 0)
                    return false;
            }
            else
            {
                if (lowerCmp <= 0)
                    return false;
            }

            int upperCmp = CompareDecimalToBinary(decimalDigits, decimalScale, upperBoundary, binaryBoundaryExponent);
            if (acceptBoundary)
                return upperCmp <= 0;

            return upperCmp < 0;
        }
        private static unsafe int ComparePositiveBinaryFloatToPowerOf10(ulong mantissa, int binaryExponent, int decimalExponent)
        {
            uint* storage = stackalloc uint[DoubleFormatBigUIntMaxWords * 2];

            BigUIntScratch left = CreateBigUIntScratch(storage);
            BigUIntScratch right = CreateBigUIntScratch(storage + DoubleFormatBigUIntMaxWords);

            BigUIntSetUInt64(ref left, mantissa);
            BigUIntSetUInt64(ref right, 1UL);

            if (decimalExponent >= 0)
            {
                if (binaryExponent >= 0)
                {
                    BigUIntShiftLeft(ref left, binaryExponent);
                    BigUIntMultiplyPow10(ref right, decimalExponent);
                }
                else
                {
                    BigUIntMultiplyPow10(ref right, decimalExponent);
                    BigUIntShiftLeft(ref right, -binaryExponent);
                }
            }
            else
            {
                BigUIntMultiplyPow10(ref left, -decimalExponent);
                if (binaryExponent >= 0)
                    BigUIntShiftLeft(ref left, binaryExponent);
                else
                    BigUIntShiftLeft(ref right, -binaryExponent);
            }

            return BigUIntCompare(ref left, ref right);
        }
        private static unsafe int CompareDecimalToBinary(ulong decimalDigits, int decimalScale, ulong binaryMantissa, int binaryExponent)
        {
            uint* storage = stackalloc uint[DoubleFormatBigUIntMaxWords * 2];

            BigUIntScratch left = CreateBigUIntScratch(storage);
            BigUIntScratch right = CreateBigUIntScratch(storage + DoubleFormatBigUIntMaxWords);

            BigUIntSetUInt64(ref left, decimalDigits);
            BigUIntSetUInt64(ref right, binaryMantissa);

            if (decimalScale >= 0)
                BigUIntMultiplyPow10(ref left, decimalScale);
            else
                BigUIntMultiplyPow10(ref right, -decimalScale);

            if (binaryExponent >= 0)
                BigUIntShiftLeft(ref right, binaryExponent);
            else
                BigUIntShiftLeft(ref left, -binaryExponent);

            return BigUIntCompare(ref left, ref right);
        }
        private static unsafe BigUIntScratch CreateBigUIntScratch(uint* storage)
            => new BigUIntScratch
            {
                Words = storage,
                Length = 0,
                Capacity = DoubleFormatBigUIntMaxWords,
            };
        private static unsafe void BigUIntSetUInt64(ref BigUIntScratch value, ulong source)
        {
            BigUIntClear(ref value);

            if (source == 0UL)
                return;

            value.Words[0] = (uint)source;
            uint hi = (uint)(source >> 32);
            if (hi != 0U)
            {
                BigUIntEnsureCapacity(ref value, 2);
                value.Words[1] = hi;
                value.Length = 2;
            }
            else
            {
                value.Length = 1;
            }
        }
        private static unsafe void BigUIntClear(ref BigUIntScratch value)
        {
            for (int i = 0; i < value.Length; i++)
                value.Words[i] = 0U;

            value.Length = 0;
        }
        private static unsafe void BigUIntCopy(ref BigUIntScratch destination, ref BigUIntScratch source)
        {
            BigUIntClear(ref destination);
            BigUIntEnsureCapacity(ref destination, source.Length);

            for (int i = 0; i < source.Length; i++)
                destination.Words[i] = source.Words[i];

            destination.Length = source.Length;
        }
        private static unsafe void BigUIntEnsureCapacity(ref BigUIntScratch value, int required)
        {
            if (required > value.Capacity)
                throw new OverflowException();
        }
        private static unsafe void BigUIntNormalize(ref BigUIntScratch value)
        {
            while (value.Length > 0 && value.Words[value.Length - 1] == 0U)
                value.Length--;
        }
        private static unsafe int BigUIntBitLength(ref BigUIntScratch value)
        {
            BigUIntNormalize(ref value);
            if (value.Length == 0)
                return 0;

            uint top = value.Words[value.Length - 1];
            int bits = 32;
            while ((top & 0x8000_0000U) == 0U)
            {
                bits--;
                top <<= 1;
            }

            return (value.Length - 1) * 32 + bits;
        }
        private static unsafe int BigUIntCompare(ref BigUIntScratch left, ref BigUIntScratch right)
        {
            BigUIntNormalize(ref left);
            BigUIntNormalize(ref right);

            if (left.Length != right.Length)
                return left.Length < right.Length ? -1 : 1;

            for (int i = left.Length - 1; i >= 0; i--)
            {
                uint l = left.Words[i];
                uint r = right.Words[i];
                if (l != r)
                    return l < r ? -1 : 1;
            }

            return 0;
        }
        private static unsafe void BigUIntShiftLeft(ref BigUIntScratch value, int shift)
        {
            if (shift == 0 || value.Length == 0)
                return;

            if (shift < 0)
                throw new ArgumentOutOfRangeException("shift");

            int wordShift = shift >> 5;
            int bitShift = shift & 31;

            if (bitShift != 0)
            {
                BigUIntEnsureCapacity(ref value, value.Length + 1);

                int carryShift = 32 - bitShift;
                uint carry = 0U;
                for (int i = 0; i < value.Length; i++)
                {
                    uint current = value.Words[i];
                    value.Words[i] = (current << bitShift) | carry;
                    carry = current >> carryShift;
                }

                if (carry != 0U)
                    value.Words[value.Length++] = carry;
            }

            if (wordShift != 0)
            {
                BigUIntEnsureCapacity(ref value, value.Length + wordShift);

                for (int i = value.Length - 1; i >= 0; i--)
                    value.Words[i + wordShift] = value.Words[i];

                for (int i = 0; i < wordShift; i++)
                    value.Words[i] = 0U;

                value.Length += wordShift;
            }
        }
        private static unsafe void BigUIntTruncateToLowBits(ref BigUIntScratch value, int bitCount)
        {
            if (bitCount <= 0)
            {
                BigUIntClear(ref value);
                return;
            }

            int keepLength = (bitCount + 31) >> 5;
            if (keepLength < value.Length)
            {
                for (int i = keepLength; i < value.Length; i++)
                    value.Words[i] = 0U;

                value.Length = keepLength;
            }

            int usedBitsInTopWord = bitCount & 31;
            if (usedBitsInTopWord != 0 && value.Length != 0)
            {
                uint mask = (1U << usedBitsInTopWord) - 1U;
                value.Words[value.Length - 1] &= mask;
            }

            BigUIntNormalize(ref value);
        }
        private static unsafe void BigUIntMultiplyByUInt32(ref BigUIntScratch value, uint multiplier)
        {
            BigUIntNormalize(ref value);

            if (value.Length == 0 || multiplier == 1U)
                return;

            if (multiplier == 0U)
            {
                BigUIntClear(ref value);
                return;
            }

            BigUIntEnsureCapacity(ref value, value.Length + 1);

            ulong carry = 0UL;
            for (int i = 0; i < value.Length; i++)
            {
                ulong product = (ulong)value.Words[i] * multiplier + carry;
                value.Words[i] = (uint)product;
                carry = product >> 32;
            }

            if (carry != 0UL)
                value.Words[value.Length++] = (uint)carry;
        }
        private static void BigUIntMultiplyPow10(ref BigUIntScratch value, int exponent)
        {
            if (exponent < 0)
                throw new ArgumentOutOfRangeException("exponent");

            while (exponent >= 9)
            {
                BigUIntMultiplyByUInt32(ref value, 1000000000U);
                exponent -= 9;
            }

            if (exponent != 0)
                BigUIntMultiplyByUInt32(ref value, Pow10UInt32(exponent));
        }
        private static uint Pow10UInt32(int exponent)
        {
            switch (exponent)
            {
                case 0: return 1U;
                case 1: return 10U;
                case 2: return 100U;
                case 3: return 1000U;
                case 4: return 10000U;
                case 5: return 100000U;
                case 6: return 1000000U;
                case 7: return 10000000U;
                case 8: return 100000000U;
                case 9: return 1000000000U;
                default: throw new ArgumentOutOfRangeException("exponent");
            }
        }
        private static unsafe void BigUIntSubtract(ref BigUIntScratch left, ref BigUIntScratch right)
        {
            ulong borrow = 0UL;
            int rightLength = right.Length;

            for (int i = 0; i < left.Length; i++)
            {
                ulong subtrahend = (i < rightLength ? right.Words[i] : 0UL) + borrow;
                ulong minuend = left.Words[i];
                left.Words[i] = (uint)(minuend - subtrahend);
                borrow = minuend < subtrahend ? 1UL : 0UL;
            }

            if (borrow != 0UL)
                throw new InvalidOperationException();

            BigUIntNormalize(ref left);
        }
        private static unsafe ulong BigUIntDivRemPow2ToUInt64(ref BigUIntScratch numerator, int denominatorShift, ref BigUIntScratch remainder)
        {
            if (denominatorShift < 0)
                throw new ArgumentOutOfRangeException("denominatorShift");

            BigUIntCopy(ref remainder, ref numerator);
            BigUIntTruncateToLowBits(ref remainder, denominatorShift);

            int wordShift = denominatorShift >> 5;
            int bitShift = denominatorShift & 31;
            if (wordShift >= numerator.Length)
                return 0UL;

            ulong quotient = 0UL;
            int quotientWords = numerator.Length - wordShift;
            for (int i = 0; i < quotientWords; i++)
            {
                int sourceIndex = wordShift + i;
                uint word = bitShift == 0
                    ? numerator.Words[sourceIndex]
                    : numerator.Words[sourceIndex] >> bitShift;

                if (bitShift != 0 && sourceIndex + 1 < numerator.Length)
                    word |= numerator.Words[sourceIndex + 1] << (32 - bitShift);

                if (i < 2)
                {
                    quotient |= (ulong)word << (i * 32);
                }
                else if (word != 0U)
                {
                    throw new OverflowException();
                }
            }

            return quotient;
        }
        private static unsafe int BigUIntCompareTwiceToPowerOfTwo(ref BigUIntScratch value, int powerOfTwoExponent)
        {
            if (powerOfTwoExponent < 0)
                throw new ArgumentOutOfRangeException("powerOfTwoExponent");

            BigUIntNormalize(ref value);
            if (value.Length == 0)
                return -1;

            if (powerOfTwoExponent == 0)
                return 1;
            int bitLength = BigUIntBitLength(ref value);
            if (bitLength < powerOfTwoExponent)
                return -1;
            if (bitLength > powerOfTwoExponent)
                return 1;

            return BigUIntIsSingleBitSet(ref value, powerOfTwoExponent - 1) ? 0 : 1;
        }
        private static unsafe bool BigUIntIsSingleBitSet(ref BigUIntScratch value, int bitIndex)
        {
            if (bitIndex < 0)
                return false;

            int wordIndex = bitIndex >> 5;
            int bitInWord = bitIndex & 31;
            if (value.Length != wordIndex + 1)
                return false;

            if (value.Words[wordIndex] != (1U << bitInWord))
                return false;

            for (int i = 0; i < wordIndex; i++)
            {
                if (value.Words[i] != 0U)
                    return false;
            }

            return true;
        }
        private static unsafe ulong BigUIntDivRemToUInt64(
            ref BigUIntScratch numerator,
            ref BigUIntScratch denominator,
            ref BigUIntScratch remainder,
            ref BigUIntScratch shiftedDenominator)
        {
            if (denominator.Length == 0)
                throw new DivideByZeroException();

            BigUIntCopy(ref remainder, ref numerator);

            if (BigUIntCompare(ref remainder, ref denominator) < 0)
                return 0UL;

            int maxShift = BigUIntBitLength(ref remainder) - BigUIntBitLength(ref denominator);
            if (maxShift >= 64)
                throw new OverflowException();

            BigUIntCopy(ref shiftedDenominator, ref denominator);
            BigUIntShiftLeft(ref shiftedDenominator, maxShift);

            ulong quotient = 0UL;
            for (int shift = maxShift; shift >= 0; shift--)
            {
                if (BigUIntCompare(ref remainder, ref shiftedDenominator) >= 0)
                {
                    BigUIntSubtract(ref remainder, ref shiftedDenominator);
                    quotient |= 1UL << shift;
                }
                if (shift != 0 && shiftedDenominator.Length != 0)
                {
                    uint carry = 0U;
                    for (int i = shiftedDenominator.Length - 1; i >= 0; i--)
                    {
                        uint current = shiftedDenominator.Words[i];
                        shiftedDenominator.Words[i] = (current >> 1) | (carry << 31);
                        carry = current & 1U;
                    }
                    BigUIntNormalize(ref shiftedDenominator);
                }
            }

            return quotient;
        }
        private static unsafe int FormatShortestDoubleToBuffer(bool negative, ulong digits, int decimalScale, char* destination, int destinationLength)
        {
            char* digitBuffer = stackalloc char[24];
            int digitCount = UInt64ToDecimalDigits(digits, digitBuffer + 24);
            char* digitStart = digitBuffer + 24 - digitCount;

            int scientificExponent = digitCount + decimalScale - 1;
            if (scientificExponent >= -4 && scientificExponent < digitCount)
                return FormatFixedDecimalToBuffer(negative, digitStart, digitCount, decimalScale, destination, destinationLength);

            return FormatScientificDecimalToBuffer(negative, digitStart, digitCount, scientificExponent, destination, destinationLength);
        }
        private static unsafe int UInt64ToDecimalDigits(ulong value, char* end)
        {
            char* p = end;
            ulong v = value;
            do
            {
                ulong digit = v % 10UL;
                v /= 10UL;
                *--p = (char)('0' + digit);
            } while (v != 0UL);

            return (int)(end - p);
        }
        private static unsafe int FormatFixedDecimalToBuffer(bool negative, char* digits, int digitCount, int decimalScale, char* destination, int destinationLength)
        {
            int decimalPoint = digitCount + decimalScale;
            int signLength = negative ? 1 : 0;
            int length;

            if (decimalPoint <= 0)
                length = signLength + 2 + (-decimalPoint) + digitCount;
            else if (decimalPoint >= digitCount)
                length = signLength + decimalPoint;
            else
                length = signLength + digitCount + 1;

            int pos = 0;

            if (negative)
                destination[pos++] = '-';

            if (decimalPoint <= 0)
            {
                destination[pos++] = '0';
                destination[pos++] = '.';

                int zeroCount = -decimalPoint;
                for (int i = 0; i < zeroCount; i++)
                    destination[pos++] = '0';

                for (int i = 0; i < digitCount; i++)
                    destination[pos++] = digits[i];

                return length;
            }

            if (decimalPoint >= digitCount)
            {
                for (int i = 0; i < digitCount; i++)
                    destination[pos++] = digits[i];

                for (int i = digitCount; i < decimalPoint; i++)
                    destination[pos++] = '0';

                return length;
            }

            for (int i = 0; i < decimalPoint; i++)
                destination[pos++] = digits[i];

            destination[pos++] = '.';

            for (int i = decimalPoint; i < digitCount; i++)
                destination[pos++] = digits[i];

            return length;
        }
        private static unsafe int FormatScientificDecimalToBuffer(bool negative, char* digits, int digitCount, int scientificExponent, char* destination, int destinationLength)
        {
            char* exponentBuffer = stackalloc char[8];
            uint exponentMagnitude = scientificExponent < 0 ? (uint)(-scientificExponent) : (uint)scientificExponent;
            int exponentMagnitudeDigitCount = UInt32ToDecimalDigits(exponentMagnitude, exponentBuffer + 8);
            int exponentDigitCount = exponentMagnitudeDigitCount < 2 ? 2 : exponentMagnitudeDigitCount;

            int signLength = negative ? 1 : 0;
            int significandLength = digitCount == 1 ? 1 : digitCount + 1;
            int length = signLength + significandLength + 2 + exponentDigitCount;

            int pos = 0;

            if (negative)
                destination[pos++] = '-';

            destination[pos++] = digits[0];
            if (digitCount != 1)
            {
                destination[pos++] = '.';
                for (int i = 1; i < digitCount; i++)
                    destination[pos++] = digits[i];
            }

            destination[pos++] = 'E';
            destination[pos++] = scientificExponent < 0 ? '-' : '+';

            int leadingZeroCount = exponentDigitCount - exponentMagnitudeDigitCount;
            for (int i = 0; i < leadingZeroCount; i++)
                destination[pos++] = '0';

            char* exponentStart = exponentBuffer + 8 - exponentMagnitudeDigitCount;
            for (int i = 0; i < exponentMagnitudeDigitCount; i++)
                destination[pos++] = exponentStart[i];

            return length;
        }
        private static unsafe int UInt32ToDecimalDigits(uint value, char* end)
        {
            char* p = end;
            uint v = value;
            do
            {
                uint digit = v % 10U;
                v /= 10U;
                *--p = (char)('0' + digit);
            } while (v != 0U);

            return (int)(end - p);
        }
        private static string FormatExponent(int exponent)
        {
            bool negative = exponent < 0;
            uint magnitude = negative ? (uint)(-exponent) : (uint)exponent;
            string digits = UInt32ToString(magnitude);

            if (magnitude < 10U)
                digits = "0" + digits;

            return negative ? ("E-" + digits) : ("E+" + digits);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SkipWhiteSpace(ref char p, int i, int len)
        {
            while (i < len && Char.IsWhiteSpace(System.Runtime.CompilerServices.Unsafe.Add<char>(ref p, i)))
                i++;
            return i;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SkipWhiteSpace(ReadOnlySpan<char> s, int i)
        {
            int len = s.Length;
            while (i < len && Char.IsWhiteSpace(s[i]))
                i++;
            return i;
        }

        internal static ParseStatus TryParseInt32(string s, out int result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseInt32(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseUInt32(string s, out uint result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseUInt32(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseInt64(string s, out long result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseInt64(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseUInt64(string s, out ulong result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseUInt64(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseInt16(string s, out short result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseInt16(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseUInt16(string s, out ushort result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseUInt16(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseSByte(string s, out sbyte result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseSByte(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseByte(string s, out byte result)
        {
            result = 0;
            if ((object)s == null) return ParseStatus.Format;
            return TryParseByte(s.AsSpan(), out result);
        }

        internal static ParseStatus TryParseInt32(ReadOnlySpan<char> s, out int result)
        {
            result = 0;

            int len = s.Length;
            if (len == 0) return ParseStatus.Format;

            int i = SkipWhiteSpace(s, 0);
            if (i >= len) return ParseStatus.Format;

            bool neg = false;
            char c = s[i];
            if (c == '+' || c == '-')
            {
                neg = (c == '-');
                i++;
                if (i >= len) return ParseStatus.Format;
            }

            uint limit = neg ? 2147483648u : 2147483647u;
            uint acc = 0;
            bool any = false;

            while (i < len)
            {
                c = s[i];
                uint digit = (uint)(c - '0');
                if (digit > 9u) break;

                any = true;

                if (acc > (limit - digit) / 10u)
                    return ParseStatus.Overflow;

                acc = acc * 10u + digit;
                i++;
            }

            if (!any) return ParseStatus.Format;

            i = SkipWhiteSpace(s, i);
            if (i != len) return ParseStatus.Format;

            if (neg)
            {
                if (acc == 2147483648u)
                    result = unchecked((int)0x80000000);
                else
                    result = -(int)acc;
            }
            else
            {
                result = (int)acc;
            }

            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseUInt32(ReadOnlySpan<char> s, out uint result)
        {
            result = 0;

            int len = s.Length;
            if (len == 0) return ParseStatus.Format;

            int i = SkipWhiteSpace(s, 0);
            if (i >= len) return ParseStatus.Format;

            char c = s[i];
            if (c == '+')
            {
                i++;
                if (i >= len) return ParseStatus.Format;
            }
            else if (c == '-')
            {
                return ParseStatus.Format;
            }

            uint acc = 0;
            bool any = false;

            while (i < len)
            {
                c = s[i];
                uint digit = (uint)(c - '0');
                if (digit > 9u) break;

                any = true;

                if (acc > (uint.MaxValue - digit) / 10u)
                    return ParseStatus.Overflow;

                acc = acc * 10u + digit;
                i++;
            }

            if (!any) return ParseStatus.Format;

            i = SkipWhiteSpace(s, i);
            if (i != len) return ParseStatus.Format;

            result = acc;
            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseInt64(ReadOnlySpan<char> s, out long result)
        {
            result = 0;

            int len = s.Length;
            if (len == 0) return ParseStatus.Format;

            int i = SkipWhiteSpace(s, 0);
            if (i >= len) return ParseStatus.Format;

            bool neg = false;
            char c = s[i];
            if (c == '+' || c == '-')
            {
                neg = (c == '-');
                i++;
                if (i >= len) return ParseStatus.Format;
            }

            ulong limit = neg ? 9223372036854775808UL : 9223372036854775807UL;
            ulong acc = 0;
            bool any = false;

            while (i < len)
            {
                c = s[i];
                ulong digit = (ulong)(c - '0');
                if (digit > 9UL) break;

                any = true;

                if (acc > (limit - digit) / 10UL)
                    return ParseStatus.Overflow;

                acc = acc * 10UL + digit;
                i++;
            }

            if (!any) return ParseStatus.Format;

            i = SkipWhiteSpace(s, i);
            if (i != len) return ParseStatus.Format;

            if (neg)
            {
                if (acc == 9223372036854775808UL)
                    result = unchecked((long)0x8000000000000000L);
                else
                    result = -(long)acc;
            }
            else
            {
                result = (long)acc;
            }

            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseUInt64(ReadOnlySpan<char> s, out ulong result)
        {
            result = 0;

            int len = s.Length;
            if (len == 0) return ParseStatus.Format;

            int i = SkipWhiteSpace(s, 0);
            if (i >= len) return ParseStatus.Format;

            char c = s[i];
            if (c == '+')
            {
                i++;
                if (i >= len) return ParseStatus.Format;
            }
            else if (c == '-')
            {
                return ParseStatus.Format;
            }

            ulong acc = 0;
            bool any = false;

            while (i < len)
            {
                c = s[i];
                ulong digit = (ulong)(c - '0');
                if (digit > 9UL) break;

                any = true;

                if (acc > (ulong.MaxValue - digit) / 10UL)
                    return ParseStatus.Overflow;

                acc = acc * 10UL + digit;
                i++;
            }

            if (!any) return ParseStatus.Format;

            i = SkipWhiteSpace(s, i);
            if (i != len) return ParseStatus.Format;

            result = acc;
            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseInt16(ReadOnlySpan<char> s, out short result)
        {
            result = 0;
            int tmp;
            var st = TryParseInt32(s, out tmp);
            if (st != ParseStatus.OK) return st;
            if (tmp < -32768 || tmp > 32767) return ParseStatus.Overflow;
            result = (short)tmp;
            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseUInt16(ReadOnlySpan<char> s, out ushort result)
        {
            result = 0;
            uint tmp;
            var st = TryParseUInt32(s, out tmp);
            if (st != ParseStatus.OK) return st;
            if (tmp > 65535u) return ParseStatus.Overflow;
            result = (ushort)tmp;
            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseSByte(ReadOnlySpan<char> s, out sbyte result)
        {
            result = 0;
            int tmp;
            var st = TryParseInt32(s, out tmp);
            if (st != ParseStatus.OK) return st;
            if (tmp < -128 || tmp > 127) return ParseStatus.Overflow;
            result = (sbyte)tmp;
            return ParseStatus.OK;
        }

        internal static ParseStatus TryParseByte(ReadOnlySpan<char> s, out byte result)
        {
            result = 0;
            uint tmp;
            var st = TryParseUInt32(s, out tmp);
            if (st != ParseStatus.OK) return st;
            if (tmp > 255u) return ParseStatus.Overflow;
            result = (byte)tmp;
            return ParseStatus.OK;
        }
    }

    public struct Nullable<T> where T : struct
    {
        private readonly bool hasValue;
        internal T value;

        public Nullable(T value)
        {
            this.value = value;
            hasValue = true;
        }

        public readonly bool HasValue
        {
            get => hasValue;
        }

        public readonly T Value
        {
            get
            {
                if (!hasValue)
                {
                    throw new InvalidOperationException("no value");
                }
                return value;
            }
        }

        public readonly T GetValueOrDefault() => value;

        public readonly T GetValueOrDefault(T defaultValue) =>
            hasValue ? value : defaultValue;
    }

    public struct ValueTuple : ITuple
    {
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is ValueTuple;
        }
        public bool Equals(ValueTuple other)
        {
            return true;
        }
        public int CompareTo(ValueTuple other)
        {
            return 0;
        }
        public override int GetHashCode()
        {
            return 0;
        }
        public override string ToString()
        {
            return "()";
        }
        int ITuple.Length => 0;
        object? ITuple.this[int index] => throw new IndexOutOfRangeException();
    }
    public struct ValueTuple<T1> : ITuple
    {
        public T1 Item1;

        public ValueTuple(T1 item1)
        {
            Item1 = item1;
        }
        int ITuple.Length => 1;
        object? ITuple.this[int index]
        {
            get
            {
                if (index != 0)
                {
                    throw new IndexOutOfRangeException();
                }
                return Item1;
            }
        }

        public override int GetHashCode()
        {
            return Item1?.GetHashCode() ?? 0;
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2> : ITuple
    {
        public T1 Item1;
        public T2 Item2;

        public ValueTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }
        int ITuple.Length => 2;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0);
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2, T3> : ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;

        public ValueTuple(T1 item1, T2 item2, T3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }
        int ITuple.Length => 3;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0,
                                    Item3?.GetHashCode() ?? 0);
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2, T3, T4> : ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
        }
        int ITuple.Length => 4;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                3 => Item4,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0,
                                    Item3?.GetHashCode() ?? 0,
                                    Item4?.GetHashCode() ?? 0);
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() + ", " + Item4?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2, T3, T4, T5> : ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
        }
        int ITuple.Length => 5;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                3 => Item4,
                4 => Item5,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0,
                                    Item3?.GetHashCode() ?? 0,
                                    Item4?.GetHashCode() ?? 0,
                                    Item5?.GetHashCode() ?? 0);
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() +
                ", " + Item4?.ToString() + ", " + Item5?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2, T3, T4, T5, T6> : ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
        }
        int ITuple.Length => 6;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                3 => Item4,
                4 => Item5,
                5 => Item6,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0,
                                    Item3?.GetHashCode() ?? 0,
                                    Item4?.GetHashCode() ?? 0,
                                    Item5?.GetHashCode() ?? 0,
                                    Item6?.GetHashCode() ?? 0);
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() +
                ", " + Item4?.ToString() + ", " + Item5?.ToString() + ", " + Item6?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2, T3, T4, T5, T6, T7> : ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;
        public T7 Item7;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            Item7 = item7;
        }
        int ITuple.Length => 7;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                3 => Item4,
                4 => Item5,
                5 => Item6,
                6 => Item7,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0,
                                    Item3?.GetHashCode() ?? 0,
                                    Item4?.GetHashCode() ?? 0,
                                    Item5?.GetHashCode() ?? 0,
                                    Item6?.GetHashCode() ?? 0,
                                    Item7?.GetHashCode() ?? 0);
        }
        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() +
                ", " + Item4?.ToString() + ", " + Item5?.ToString() + ", " + Item6?.ToString() + ", " + Item7?.ToString() + ")";
        }
    }
    public struct ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> : ITuple
        where TRest : struct
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;
        public T7 Item7;
        public TRest Rest;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, TRest rest)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            Item7 = item7;
            Rest = rest;
        }
        int ITuple.Length => 8;
        object? ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                3 => Item4,
                4 => Item5,
                5 => Item6,
                6 => Item7,
                7 => Rest,
                _ => throw new IndexOutOfRangeException(),
            };

        public override int GetHashCode()
        {
            return HashCode.Combine(Item1?.GetHashCode() ?? 0,
                                    Item2?.GetHashCode() ?? 0,
                                    Item3?.GetHashCode() ?? 0,
                                    Item4?.GetHashCode() ?? 0,
                                    Item5?.GetHashCode() ?? 0,
                                    Item6?.GetHashCode() ?? 0,
                                    Item7?.GetHashCode() ?? 0);
        }

        public override string ToString()
        {
            return "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() +
                ", " + Item4?.ToString() + ", " + Item5?.ToString() + ", " + Item6?.ToString() + ", " + Item7?.ToString() + ", " + Rest.ToString() + ")";
        }
    }

    public readonly struct IntPtr
    {
        private readonly nint _value;

        public static readonly nint Zero = 0;

        public IntPtr(int value)
        {
            _value = value;
        }

        public IntPtr(long value)
        {
#if TARGET_64BIT
            _value = (nint)value;
#else
            _value = checked((nint)value);
#endif                
        }

        public unsafe IntPtr(void* value)
        {
            _value = (nint)value;
        }

        public long ToInt64() => _value;


        public static int Size
        {
#if TARGET_64BIT
            get => 8;
#else
            get => 4;
#endif

        }

        public static nint MaxValue
        {
#if TARGET_64BIT
            get => unchecked((nint)0x7fffffffffffffffL);
#else
            get => unchecked((nint)0x7fffffff);
#endif
        }

        public static nint MinValue
        {
#if TARGET_64BIT
get => unchecked((nint)(unchecked((long)0x8000000000000000L)));
#else
            get => unchecked((nint)(unchecked((int)0x80000000)));
#endif
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => (obj is nint other) && Equals(other);
        public override int GetHashCode()
        {
#if TARGET_64BIT
            long value = _value;
            return value.GetHashCode();
#else
            return (int)_value;
#endif
        }
    }
    public readonly struct UIntPtr
    {
        private readonly nuint _value;

        public static readonly nuint Zero = 0;

        public UIntPtr(uint value)
        {
            _value = value;
        }

        public UIntPtr(ulong value)
        {
#if TARGET_64BIT
            _value = (nuint)value;
#else
            _value = checked((nuint)value);
#endif
        }

        public unsafe UIntPtr(void* value)
        {
            _value = (nuint)value;
        }

        public static int Size
        {
#if TARGET_64BIT
            get => 8;
#else
            get => 4;
#endif
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => (obj is nuint other) && Equals(other);
        public override int GetHashCode()
        {
#if TARGET_64BIT
            ulong value = _value;
            return value.GetHashCode();
#else
            return (int)_value;
#endif
        }
    }

    public readonly ref struct Span<T>
    {
        internal readonly ref T _reference;
        internal readonly int _length;
        public int Length => _length;
        public bool IsEmpty => _length == 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Span(void* pointer, int length)
        {
            _reference = ref *(T*)pointer;
            _length = length;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span(T[] array)
        {
            if (array == null)
            {
                this = default;
                return;
            }
            if (!typeof(T).IsValueType && array.GetType() != typeof(T[]))
                throw new ArrayTypeMismatchException();

            _reference = ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference<T>(array);
            _length = array.Length;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span(T[]? array, int start, int length)
        {
            if (array == null)
            {
                if (start != 0 || length != 0)
                    throw new ArgumentOutOfRangeException();
                this = default;
                return; // returns default
            }
#if TARGET_64BIT
            if ((ulong)(uint)start + (ulong)(uint)length > (ulong)(uint)array.Length)
                throw new ArgumentOutOfRangeException();
#else
            if ((uint)start > (uint)array.Length || (uint)length > (uint)(array.Length - start))
                throw new ArgumentOutOfRangeException();
#endif
            _reference = ref Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(array), (nint)(uint)start /* force zero-extension */);
            _length = length;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span(ref T reference)
        {
            _reference = ref reference;
            _length = 1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Span(ref T reference, int length)
        {
            _reference = ref reference;
            _length = length;
        }

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)index >= (uint)_length)
                {
                    throw new IndexOutOfRangeException();
                }
                return ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref _reference, (nint)(uint)index /* force zero-extension */);
            }
        }

        /// <summary>
        /// Clears the contents of this span.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Clear()
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                SpanHelpers.ClearWithReferences(ref Unsafe.As<T, IntPtr>(ref _reference), (uint)_length * (nuint)(sizeof(T) / sizeof(nuint)));
            }
            else
            {
                SpanHelpers.ClearWithoutReferences(ref Unsafe.As<T, byte>(ref _reference), (uint)_length * (nuint)sizeof(T));
            }
        }

        /// <summary>
        /// Fills the contents of this span with the given value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Fill(T value)
        {
            SpanHelpers.Fill(ref _reference, (uint)_length, value);
        }

        /// <summary>
        /// Copies the contents of this span into destination span. If the source
        /// and destinations overlap, this method behaves as if the original values in
        /// a temporary location before the destination is overwritten.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when the destination Span is shorter than the source Span.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Span<T> destination)
        {
            if ((uint)_length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref _reference, (uint)_length);
            }
            else
            {
                throw new ArgumentException();
            }
        }

        /// <summary>
        /// Copies the contents of this span into destination span. If the source
        /// and destinations overlap, this method behaves as if the original values in
        /// a temporary location before the destination is overwritten.
        /// </summary>
        /// <returns>If the destination span is shorter than the source span, this method
        /// return false and no data is written to the destination.</returns>
        public bool TryCopyTo(Span<T> destination)
        {
            bool retVal = false;
            if ((uint)_length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref _reference, (uint)_length);
                retVal = true;
            }
            return retVal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Span<T>(T[] array) => new Span<T>(array);
        public static bool operator ==(Span<T> left, Span<T> right) =>
            left._length == right._length &&
            System.Runtime.CompilerServices.Unsafe.AreSame<T>(ref left._reference, ref right._reference);
        public static bool operator !=(Span<T> left, Span<T> right) => !(left == right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> Slice(int start)
        {
            if ((uint)start > (uint)_length)
            {
                throw new ArgumentOutOfRangeException();
            }
            return new Span<T>(ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref _reference, (nint)(uint)start /* force zero-extension */), _length - start);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> Slice(int start, int length)
        {
#if TARGET_64BIT
            if ((ulong)(uint)start + (ulong)(uint)length > (ulong)(uint)_length)
            {
                throw new ArgumentOutOfRangeException();
            }
#else
            if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
            {
                throw new ArgumentOutOfRangeException();
            }
#endif
            return new Span<T>(ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref _reference, (nint)(uint)start /* force zero-extension */), length);
        }


    }
    public readonly ref struct ReadOnlySpan<T>
    {
        internal readonly ref T _reference;
        private readonly int _length;
        public int Length => _length;
        public bool IsEmpty => _length == 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan(T[] array)
        {
            if (array == null)
                return;
            _reference = ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference<T>(array);
            _length = array.Length;
        }

        public unsafe ReadOnlySpan(void* pointer, int length)
        {
            _reference = ref *(T*)pointer;
            _length = length;
        }
        public ReadOnlySpan(ref readonly T reference)
        {
            _reference = ref System.Runtime.CompilerServices.Unsafe.AsRef<T>(in reference);
            _length = 1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan(ref T reference, int length)
        {
            _reference = ref reference;
            _length = length;
        }
        public ref readonly T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)index >= (uint)_length)
                    throw new IndexOutOfRangeException();
                return ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref _reference, (nint)(uint)index /* force zero-extension */);
            }
        }

        /// <summary>
        /// Copies the contents of this read-only span into destination span. If the source
        /// and destinations overlap, this method behaves as if the original values in
        /// a temporary location before the destination is overwritten.
        /// </summary>
        /// <param name="destination">The span to copy items into.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when the destination Span is shorter than the source Span.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Span<T> destination)
        {
            if ((uint)_length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref _reference, (uint)_length);
            }
            else
            {
                throw new ArgumentException();
            }
        }

        /// <summary>
        /// Copies the contents of this read-only span into destination span. If the source
        /// and destinations overlap, this method behaves as if the original values in
        /// a temporary location before the destination is overwritten.
        /// </summary>
        /// <returns>If the destination span is shorter than the source span, this method
        /// return false and no data is written to the destination.</returns>
        /// <param name="destination">The span to copy items into.</param>
        public bool TryCopyTo(Span<T> destination)
        {
            bool retVal = false;
            if ((uint)_length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref _reference, (uint)_length);
                retVal = true;
            }
            return retVal;
        }

        /// <summary>
        /// Returns false if left and right point at the same memory and have the same length.  Note that
        /// this does *not* check to see if the *contents* are equal.
        /// </summary>
        public static bool operator !=(ReadOnlySpan<T> left, ReadOnlySpan<T> right) => !(left == right);

        public static bool operator ==(ReadOnlySpan<T> left, ReadOnlySpan<T> right) =>
            left._length == right._length &&
            Unsafe.AreSame(ref left._reference, ref right._reference);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlySpan<T>(T[] array) => new ReadOnlySpan<T>(array);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlySpan<T>(Span<T> span) => new ReadOnlySpan<T>(ref span._reference, span.Length);

        /// <summary>
        /// Returns a 0-length read-only span whose base is the null pointer.
        /// </summary>
        public static ReadOnlySpan<T> Empty => default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> Slice(int start)
        {
            if ((uint)start > (uint)_length)
            {
                throw new IndexOutOfRangeException();
            }

            return new ReadOnlySpan<T>(ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref _reference, (nint)(uint)start), _length - start);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> Slice(int start, int length)
        {
#if TARGET_64BIT
            if ((ulong)(uint)start + (ulong)(uint)length > (ulong)(uint)_length)
            {
                throw new ArgumentOutOfRangeException();
            }
#else
            if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
            {
                throw new ArgumentOutOfRangeException();
            }
#endif
            return new ReadOnlySpan<T>(ref System.Runtime.CompilerServices.Unsafe.Add<T>(ref _reference, (nint)(uint)start), length);
        }
    }
    public readonly struct ReadOnlyMemory<T>
    {
        internal readonly object _object;
        internal readonly int _index;
        internal readonly int _length;

        internal const int RemoveFlagsBitMask = 0x7FFFFFFF;

        public ReadOnlyMemory(T[] array)
        {
            if (array == null)
            {
                //this = default;
                return; // returns default
            }

            _object = array;
            _index = 0;
            _length = array.Length;
        }
        internal ReadOnlyMemory(object obj, int start, int length)
        {
            _object = obj;
            _index = start;
            _length = length;
        }
    }

    internal static class SpanHelpers
    {
#if TARGET_ARM64 || TARGET_LOONGARCH64
        private const ulong MemmoveNativeThreshold = ulong.MaxValue;
#elif TARGET_ARM
        private const nuint MemmoveNativeThreshold = 512;
#else
        private const nuint MemmoveNativeThreshold = 2048;
#endif
        private const nuint ZeroMemoryNativeThreshold = 1024;

#if HAS_CUSTOM_BLOCKS
        [StructLayout(LayoutKind.Sequential, Size = 16)]
        private struct Block16 {}

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct Block64 {}
#endif // HAS_CUSTOM_BLOCKS

        public static unsafe void ClearWithReferences(ref IntPtr ip, nuint pointerSizeLength)
        {
            // First write backward 8 natural words at a time.
            // Writing backward allows us to get away with only simple modifications to the
            // mov instruction's base and index registers between loop iterations.

            for (; pointerSizeLength >= 8; pointerSizeLength -= 8)
            {
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -1) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -2) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -3) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -4) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -5) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -6) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -7) = default;
                Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -8) = default;
            }

            // The logic below works by trying to minimize the number of branches taken for any
            // given range of lengths. For example, the lengths [ 4 .. 7 ] are handled by a single
            // branch, [ 2 .. 3 ] are handled by a single branch, and [ 1 ] is handled by a single
            // branch.
            //
            // We can write both forward and backward as a perf improvement. For example,
            // the lengths [ 4 .. 7 ] can be handled by zeroing out the first four natural
            // words and the last 3 natural words. In the best case (length = 7), there are
            // no overlapping writes. In the worst case (length = 4), there are three
            // overlapping writes near the middle of the buffer. In perf testing, the
            // penalty for performing duplicate writes is less expensive than the penalty
            // for complex branching.

            if (pointerSizeLength >= 4)
            {
                goto Write4To7;
            }
            else if (pointerSizeLength >= 2)
            {
                goto Write2To3;
            }
            else if (pointerSizeLength > 0)
            {
                goto Write1;
            }
            else
            {
                return; // nothing to write
            }

        Write4To7:
            // Write first four and last three.
            Unsafe.Add(ref ip, 2) = default;
            Unsafe.Add(ref ip, 3) = default;
            Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -3) = default;
            Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -2) = default;

        Write2To3:
            // Write first two and last one.
            Unsafe.Add(ref ip, 1) = default;
            Unsafe.Add(ref Unsafe.Add(ref ip, (nint)pointerSizeLength), -1) = default;

        Write1:
            // Write only element.
            ip = default;
        }

        public static void ClearWithoutReferences(ref byte dest, nuint len)
        {
            if (len == 0)
                return;

            ref byte destEnd = ref Unsafe.Add(ref dest, len);

            if (len <= 16)
                goto MZER02;
            if (len > 64)
                goto MZER05;

        MZER00:
            // Clear bytes which are multiples of 16 and leave the remainder for MZER01 to handle.
#if HAS_CUSTOM_BLOCKS
            Unsafe.WriteUnaligned<Block16>(ref dest, default);
#elif TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref dest, 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 8), 0);
#else
            Unsafe.WriteUnaligned<int>(ref dest, 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 4), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 8), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 12), 0);
#endif
            if (len <= 32)
                goto MZER01;
#if HAS_CUSTOM_BLOCKS
            Unsafe.WriteUnaligned<Block16>(ref Unsafe.Add(ref dest, 16), default);
#elif TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 16), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 24), 0);
#else
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 16), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 20), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 24), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 28), 0);
#endif
            if (len <= 48)
                goto MZER01;
#if HAS_CUSTOM_BLOCKS
            Unsafe.WriteUnaligned<Block16>(ref Unsafe.Add(ref dest, 32), default);
#elif TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 32), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 40), 0);
#else
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 32), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 36), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 40), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 44), 0);
#endif

        MZER01:
            // Unconditionally clear the last 16 bytes using destEnd and return.
#if HAS_CUSTOM_BLOCKS
            Unsafe.WriteUnaligned<Block16>(ref Unsafe.Add(ref destEnd, -16), default);
#elif TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref destEnd, -16), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref destEnd, -8), 0);
#else
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -16), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -12), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -8), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -4), 0);
#endif
            return;

        MZER02:
            // Clear the first 8 bytes and then unconditionally clear the last 8 bytes and return.
            if ((len & 24) == 0)
                goto MZER03;
#if TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref dest, 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref destEnd, -8), 0);
#else
            Unsafe.WriteUnaligned<int>(ref dest, 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 4), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -8), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -4), 0);
#endif
            return;

        MZER03:
            // Clear the first 4 bytes and then unconditionally clear the last 4 bytes and return.
            if ((len & 4) == 0)
                goto MZER04;
            Unsafe.WriteUnaligned<int>(ref dest, 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -4), 0);
            return;

        MZER04:
            // Clear the first byte. For pending bytes, do an unconditionally clear of the last 2 bytes and return.
            if (len == 0)
                return;
            dest = 0;
            if ((len & 2) == 0)
                return;
            Unsafe.WriteUnaligned<short>(ref Unsafe.Add(ref destEnd, -2), 0);
            return;

        MZER05:
            // PInvoke to the native version when the clear length exceeds the threshold.
            if (len > ZeroMemoryNativeThreshold)
            {
                goto PInvoke;
            }

#if HAS_CUSTOM_BLOCKS
            if (len >= 256)
            {
                // Try to opportunistically align the destination below. The input isn't pinned, so the GC
                // is free to move the references. We're therefore assuming that reads may still be unaligned.
                nuint misalignedElements = 64 - Unsafe.OpportunisticMisalignment(ref dest, 64);
                Unsafe.WriteUnaligned<Block64>(ref dest, default);
                dest = ref Unsafe.Add(ref dest, misalignedElements);
                len -= misalignedElements;
            }
#endif
            // Clear 64-bytes at a time until the remainder is less than 64.
            // If remainder is greater than 16 bytes, then jump to MZER00. Otherwise, unconditionally clear the last 16 bytes and return.
            nuint n = len >> 6;

        MZER06:
#if HAS_CUSTOM_BLOCKS
            Unsafe.WriteUnaligned<Block64>(ref dest, default);
#elif TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref dest, 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 8), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 16), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 24), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 32), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 40), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 48), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref dest, 56), 0);
#else
            Unsafe.WriteUnaligned<int>(ref dest, 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 4), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 8), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 12), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 16), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 20), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 24), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 28), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 32), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 36), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 40), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 44), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 48), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 52), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 56), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref dest, 60), 0);
#endif
            dest = ref Unsafe.Add(ref dest, 64);
            n--;
            if (n != 0)
                goto MZER06;

            len %= 64;
            if (len > 16)
                goto MZER00;
#if HAS_CUSTOM_BLOCKS
            Unsafe.WriteUnaligned<Block16>(ref Unsafe.Add(ref destEnd, -16), default);
#elif TARGET_64BIT
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref destEnd, -16), 0);
            Unsafe.WriteUnaligned<long>(ref Unsafe.Add(ref destEnd, -8), 0);
#else
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -16), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -12), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -8), 0);
            Unsafe.WriteUnaligned<int>(ref Unsafe.Add(ref destEnd, -4), 0);
#endif
            return;

        PInvoke:
            // Implicit nullchecks
            _ = Unsafe.ReadUnaligned<byte>(ref dest);
            ZeroMemoryNative(ref dest, len);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static unsafe void ZeroMemoryNative(ref byte b, nuint byteLength)
        {
            fixed (byte* ptr = &b)
            {
                byte* adjustedPtr = ptr;
#if TARGET_X86 || TARGET_AMD64
                if (byteLength > 0x100)
                {
                    // memset ends up calling rep stosb if the hardware claims to support it efficiently. rep stosb is up to 2x slower
                    // on misaligned blocks. Workaround this issue by aligning the blocks passed to memset upfront.
                    Unsafe.WriteUnaligned<Block16>(ptr, default);
                    Unsafe.WriteUnaligned<Block16>(ptr + byteLength - 16, default);

                    byte* alignedEnd = (byte*)((nuint)(ptr + byteLength - 1) & ~(nuint)(16 - 1));

                    adjustedPtr = (byte*)(((nuint)ptr + 16) & ~(nuint)(16 - 1));
                    byteLength = (nuint)(alignedEnd - adjustedPtr);
                }
#endif
                memset(adjustedPtr, 0, byteLength);
            }
        }

        public static unsafe void Fill<T>(ref T refData, nuint numElements, T value)
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                goto CannotVectorize;
            }


        CannotVectorize:

            // If we reached this point, we cannot vectorize this T, or there are too few
            // elements for us to vectorize. Fall back to an unrolled loop.

            nuint i = 0;

            // Write 8 elements at a time

            if (numElements >= 8)
            {
                nuint stopLoopAtOffset = numElements & ~(nuint)7;
                do
                {
                    Unsafe.Add(ref refData, (nint)i + 0) = value;
                    Unsafe.Add(ref refData, (nint)i + 1) = value;
                    Unsafe.Add(ref refData, (nint)i + 2) = value;
                    Unsafe.Add(ref refData, (nint)i + 3) = value;
                    Unsafe.Add(ref refData, (nint)i + 4) = value;
                    Unsafe.Add(ref refData, (nint)i + 5) = value;
                    Unsafe.Add(ref refData, (nint)i + 6) = value;
                    Unsafe.Add(ref refData, (nint)i + 7) = value;
                } while ((i += 8) < stopLoopAtOffset);
            }

            // Write next 4 elements if needed

            if ((numElements & 4) != 0)
            {
                Unsafe.Add(ref refData, (nint)i + 0) = value;
                Unsafe.Add(ref refData, (nint)i + 1) = value;
                Unsafe.Add(ref refData, (nint)i + 2) = value;
                Unsafe.Add(ref refData, (nint)i + 3) = value;
                i += 4;
            }

            // Write next 2 elements if needed

            if ((numElements & 2) != 0)
            {
                Unsafe.Add(ref refData, (nint)i + 0) = value;
                Unsafe.Add(ref refData, (nint)i + 1) = value;
                i += 2;
            }

            // Write final element if needed

            if ((numElements & 1) != 0)
            {
                Unsafe.Add(ref refData, (nint)i) = value;
            }
        }

        public static bool SequenceEqual<T>(ref T first, ref T second, int length) where T : IEquatable<T>?
        {
            if (Unsafe.AreSame(ref first, ref second))
            {
                return true;
            }

            nint index = 0; // Use nint for arithmetic to avoid unnecessary 64->32->64 truncations
            T lookUp0;
            T lookUp1;
            while (length >= 8)
            {
                length -= 8;

                lookUp0 = Unsafe.Add(ref first, index);
                lookUp1 = Unsafe.Add(ref second, index);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 1);
                lookUp1 = Unsafe.Add(ref second, index + 1);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 2);
                lookUp1 = Unsafe.Add(ref second, index + 2);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 3);
                lookUp1 = Unsafe.Add(ref second, index + 3);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 4);
                lookUp1 = Unsafe.Add(ref second, index + 4);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 5);
                lookUp1 = Unsafe.Add(ref second, index + 5);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 6);
                lookUp1 = Unsafe.Add(ref second, index + 6);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 7);
                lookUp1 = Unsafe.Add(ref second, index + 7);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                index += 8;
            }

            if (length >= 4)
            {
                length -= 4;

                lookUp0 = Unsafe.Add(ref first, index);
                lookUp1 = Unsafe.Add(ref second, index);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 1);
                lookUp1 = Unsafe.Add(ref second, index + 1);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 2);
                lookUp1 = Unsafe.Add(ref second, index + 2);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                lookUp0 = Unsafe.Add(ref first, index + 3);
                lookUp1 = Unsafe.Add(ref second, index + 3);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                index += 4;
            }

            while (length > 0)
            {
                lookUp0 = Unsafe.Add(ref first, index);
                lookUp1 = Unsafe.Add(ref second, index);
                if (!(lookUp0?.Equals(lookUp1) ?? (object?)lookUp1 is null))
                {
                    return false;
                }

                index += 1;
                length--;
            }

            return true;
        }

        public static unsafe bool SequenceEqual(ref byte first, ref byte second, nuint length)
        {
            bool result;
            // Use nint for arithmetic to avoid unnecessary 64->32->64 truncations
            if (length >= (nuint)sizeof(nuint))
            {
                // Conditional jmp forward to favor shorter lengths. (See comment at "Equal:" label)
                // The longer lengths can make back the time due to branch misprediction
                // better than shorter lengths.
                goto Longer;
            }

#if TARGET_64BIT
            // On 32-bit, this will always be true since sizeof(nuint) == 4
            if (length < sizeof(uint))
#endif
            {
                uint differentBits = 0;
                nuint offset = (length & 2);
                if (offset != 0)
                {
                    differentBits = LoadUShort(ref first);
                    differentBits -= LoadUShort(ref second);
                }
                if ((length & 1) != 0)
                {
                    differentBits |= (uint)Unsafe.AddByteOffset(ref first, offset) - (uint)Unsafe.AddByteOffset(ref second, offset);
                }
                result = (differentBits == 0);
                goto Result;
            }
#if TARGET_64BIT
            else
            {
                nuint offset = length - sizeof(uint);
                uint differentBits = LoadUInt(ref first) - LoadUInt(ref second);
                differentBits |= LoadUInt(ref first, offset) - LoadUInt(ref second, offset);
                result = (differentBits == 0);
                goto Result;
            }
#endif
        Longer:
            // Only check that the ref is the same if buffers are large,
            // and hence its worth avoiding doing unnecessary comparisons
            if (!Unsafe.AreSame(ref first, ref second))
            {
                goto Vector;
            }

            // This becomes a conditional jmp forward to not favor it.
            goto Equal;

        Result:
            return result;
            // When the sequence is equal; which is the longest execution, we want it to determine that
            // as fast as possible so we do not want the early outs to be "predicted not taken" branches.
        Equal:
            return true;

        Vector:

            {
                {
                    nuint offset = 0;
                    nuint lengthToExamine = length - (nuint)sizeof(nuint);
                    // Unsigned, so it shouldn't have overflowed larger than length (rather than negative)

                    if (lengthToExamine > 0)
                    {
                        do
                        {
                            // Compare unsigned so not do a sign extend mov on 64 bit
                            if (LoadNUInt(ref first, offset) != LoadNUInt(ref second, offset))
                            {
                                goto NotEqual;
                            }
                            offset += (nuint)sizeof(nuint);
                        } while (lengthToExamine > offset);
                    }

                    // Do final compare as sizeof(nuint) from end rather than start
                    result = (LoadNUInt(ref first, lengthToExamine) == LoadNUInt(ref second, lengthToExamine));
                    goto Result;
                }
            }

        NotEqual:
            return false;
        }

        public static int SequenceCompareTo<T>(ref T first, int firstLength, ref T second, int secondLength)
            where T : IComparable<T>?
        {
            int minLength = firstLength;
            if (minLength > secondLength)
                minLength = secondLength;
            for (int i = 0; i < minLength; i++)
            {
                T lookUp = Unsafe.Add(ref second, i);
                int result = (Unsafe.Add(ref first, i)?.CompareTo(lookUp) ?? (((object?)lookUp is null) ? 0 : -1));
                if (result != 0)
                {
                    return result;
                }
            }

            return firstLength.CompareTo(secondLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort LoadUShort(ref byte start)
            => Unsafe.ReadUnaligned<ushort>(ref start);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint LoadUInt(ref byte start)
            => Unsafe.ReadUnaligned<uint>(ref start);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint LoadUInt(ref byte start, nuint offset)
            => Unsafe.ReadUnaligned<uint>(ref Unsafe.AddByteOffset(ref start, offset));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint LoadNUInt(ref byte start)
            => Unsafe.ReadUnaligned<nuint>(ref start);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static nuint LoadNUInt(ref byte start, nuint offset)
            => Unsafe.ReadUnaligned<nuint>(ref Unsafe.AddByteOffset(ref start, offset));

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern unsafe void memset(void* dest, int value, nuint len);
    }

    public static class MemoryExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<char> Trim(this ReadOnlySpan<char> span)
        {
            // Assume that in most cases input doesn't need trimming
            if (span.Length == 0 ||
                (!char.IsWhiteSpace(span[0]) && !char.IsWhiteSpace(span[^1])))
            {
                return span;
            }
            return TrimFallback(span);

            [MethodImpl(MethodImplOptions.NoInlining)]
            static ReadOnlySpan<char> TrimFallback(ReadOnlySpan<char> span)
            {
                int start = 0;
                for (; start < span.Length; start++)
                {
                    if (!char.IsWhiteSpace(span[start]))
                    {
                        break;
                    }
                }

                int end = span.Length - 1;
                for (; end > start; end--)
                {
                    if (!char.IsWhiteSpace(span[end]))
                    {
                        break;
                    }
                }
                return span.Slice(start, end - start + 1);
            }
        }

        /// <summary>
        /// Creates a new span over the target array.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<T> AsSpan<T>(this T[]? array)
        {
            return new Span<T>(array);
        }

        /// <summary>
        /// Creates a new Span over the portion of the target array beginning
        /// at 'start' index and ending at 'end' index (exclusive).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<T> AsSpan<T>(this T[]? array, int start, int length)
        {
            return new Span<T>(array, start, length);
        }


        /// <summary>
        /// Determines whether two sequences overlap in memory.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [OverloadResolutionPriority(-1)]
        public static bool Overlaps<T>(this Span<T> span, ReadOnlySpan<T> other) =>
            Overlaps((ReadOnlySpan<T>)span, other);

        /// <summary>
        /// Determines whether two sequences overlap in memory and outputs the element offset.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [OverloadResolutionPriority(-1)]
        public static bool Overlaps<T>(this Span<T> span, ReadOnlySpan<T> other, out int elementOffset) =>
            Overlaps((ReadOnlySpan<T>)span, other, out elementOffset);

        /// <summary>
        /// Determines whether two sequences overlap in memory.
        /// </summary>
        public static unsafe bool Overlaps<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> other)
        {
            if (span.IsEmpty || other.IsEmpty)
            {
                return false;
            }

            nint byteOffset = Unsafe.ByteOffset(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span),
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other));

            return (nuint)byteOffset < (nuint)((nint)span.Length * sizeof(T)) ||
                    (nuint)byteOffset > (nuint)(-((nint)other.Length * sizeof(T)));
        }

        /// <summary>
        /// Determines whether two sequences overlap in memory and outputs the element offset.
        /// </summary>
        public static unsafe bool Overlaps<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> other, out int elementOffset)
        {
            if (span.IsEmpty || other.IsEmpty)
            {
                elementOffset = 0;
                return false;
            }

            nint byteOffset = Unsafe.ByteOffset(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span),
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other));

            if ((nuint)byteOffset < (nuint)((nint)span.Length * sizeof(T)) ||
                (nuint)byteOffset > (nuint)(-((nint)other.Length * sizeof(T))))
            {
                if (byteOffset % sizeof(T) != 0)
                    throw new ArgumentException();

                elementOffset = (int)(byteOffset / sizeof(T));
                return true;
            }
            else
            {
                elementOffset = 0;
                return false;
            }
        }

        /// <summary>
        /// Determines whether two sequences are equal by comparing the elements using IEquatable{T}.Equals(T).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool SequenceEqual<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> other) where T : IEquatable<T>?
        {
            int length = span.Length;
            int otherLength = other.Length;

            //if (RuntimeHelpers.IsBitwiseEquatable<T>())
            {

            }

            return length == otherLength && SpanHelpers.SequenceEqual(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span), 
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other), length);
        }
        /// <summary>
        /// Determines the relative order of the sequences being compared by comparing the elements using IComparable{T}.CompareTo(T).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SequenceCompareTo<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> other) where T : IComparable<T>?
        {
            // Can't use IsBitwiseEquatable<T>() below because that only tells us about
            // equality checks, not about CompareTo checks.

            if (typeof(T) == typeof(byte))
                return SpanHelpers.SequenceCompareTo(
                    ref Unsafe.As<T, byte>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span)),
                    span.Length,
                    ref Unsafe.As<T, byte>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other)),
                    other.Length);

            if (typeof(T) == typeof(char))
                return SpanHelpers.SequenceCompareTo(
                    ref Unsafe.As<T, char>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span)),
                    span.Length,
                    ref Unsafe.As<T, char>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other)),
                    other.Length);

            return SpanHelpers.SequenceCompareTo(
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span), span.Length, 
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(other), other.Length);
        }
    }

    public struct HashCode
    {
        private static readonly uint s_seed = GenerateGlobalSeed();

        private const uint Prime1 = 2654435761U;
        private const uint Prime2 = 2246822519U;
        private const uint Prime3 = 3266489917U;
        private const uint Prime4 = 668265263U;
        private const uint Prime5 = 374761393U;

        private uint _v1, _v2, _v3, _v4;
        private uint _queue1, _queue2, _queue3;
        private uint _length;

        private static unsafe uint GenerateGlobalSeed()
        {
            uint result = 0u;
            //Interop.GetRandomBytes((byte*)&result, sizeof(uint));
            return result;
        }

        public static int Combine<T1>(T1 value1)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);

            uint hash = MixEmptyState();
            hash += 4;

            hash = QueueRound(hash, hc1);

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2>(T1 value1, T2 value2)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);

            uint hash = MixEmptyState();
            hash += 8;

            hash = QueueRound(hash, hc1);
            hash = QueueRound(hash, hc2);

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);
            uint hc3 = (uint)(value3?.GetHashCode() ?? 0);

            uint hash = MixEmptyState();
            hash += 12;

            hash = QueueRound(hash, hc1);
            hash = QueueRound(hash, hc2);
            hash = QueueRound(hash, hc3);

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);
            uint hc3 = (uint)(value3?.GetHashCode() ?? 0);
            uint hc4 = (uint)(value4?.GetHashCode() ?? 0);

            Initialize(out uint v1, out uint v2, out uint v3, out uint v4);

            v1 = Round(v1, hc1);
            v2 = Round(v2, hc2);
            v3 = Round(v3, hc3);
            v4 = Round(v4, hc4);

            uint hash = MixState(v1, v2, v3, v4);
            hash += 16;

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2, T3, T4, T5>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);
            uint hc3 = (uint)(value3?.GetHashCode() ?? 0);
            uint hc4 = (uint)(value4?.GetHashCode() ?? 0);
            uint hc5 = (uint)(value5?.GetHashCode() ?? 0);

            Initialize(out uint v1, out uint v2, out uint v3, out uint v4);

            v1 = Round(v1, hc1);
            v2 = Round(v2, hc2);
            v3 = Round(v3, hc3);
            v4 = Round(v4, hc4);

            uint hash = MixState(v1, v2, v3, v4);
            hash += 20;

            hash = QueueRound(hash, hc5);

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2, T3, T4, T5, T6>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);
            uint hc3 = (uint)(value3?.GetHashCode() ?? 0);
            uint hc4 = (uint)(value4?.GetHashCode() ?? 0);
            uint hc5 = (uint)(value5?.GetHashCode() ?? 0);
            uint hc6 = (uint)(value6?.GetHashCode() ?? 0);

            Initialize(out uint v1, out uint v2, out uint v3, out uint v4);

            v1 = Round(v1, hc1);
            v2 = Round(v2, hc2);
            v3 = Round(v3, hc3);
            v4 = Round(v4, hc4);

            uint hash = MixState(v1, v2, v3, v4);
            hash += 24;

            hash = QueueRound(hash, hc5);
            hash = QueueRound(hash, hc6);

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2, T3, T4, T5, T6, T7>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);
            uint hc3 = (uint)(value3?.GetHashCode() ?? 0);
            uint hc4 = (uint)(value4?.GetHashCode() ?? 0);
            uint hc5 = (uint)(value5?.GetHashCode() ?? 0);
            uint hc6 = (uint)(value6?.GetHashCode() ?? 0);
            uint hc7 = (uint)(value7?.GetHashCode() ?? 0);

            Initialize(out uint v1, out uint v2, out uint v3, out uint v4);

            v1 = Round(v1, hc1);
            v2 = Round(v2, hc2);
            v3 = Round(v3, hc3);
            v4 = Round(v4, hc4);

            uint hash = MixState(v1, v2, v3, v4);
            hash += 28;

            hash = QueueRound(hash, hc5);
            hash = QueueRound(hash, hc6);
            hash = QueueRound(hash, hc7);

            hash = MixFinal(hash);
            return (int)hash;
        }

        public static int Combine<T1, T2, T3, T4, T5, T6, T7, T8>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8)
        {
            uint hc1 = (uint)(value1?.GetHashCode() ?? 0);
            uint hc2 = (uint)(value2?.GetHashCode() ?? 0);
            uint hc3 = (uint)(value3?.GetHashCode() ?? 0);
            uint hc4 = (uint)(value4?.GetHashCode() ?? 0);
            uint hc5 = (uint)(value5?.GetHashCode() ?? 0);
            uint hc6 = (uint)(value6?.GetHashCode() ?? 0);
            uint hc7 = (uint)(value7?.GetHashCode() ?? 0);
            uint hc8 = (uint)(value8?.GetHashCode() ?? 0);

            Initialize(out uint v1, out uint v2, out uint v3, out uint v4);

            v1 = Round(v1, hc1);
            v2 = Round(v2, hc2);
            v3 = Round(v3, hc3);
            v4 = Round(v4, hc4);

            v1 = Round(v1, hc5);
            v2 = Round(v2, hc6);
            v3 = Round(v3, hc7);
            v4 = Round(v4, hc8);

            uint hash = MixState(v1, v2, v3, v4);
            hash += 32;

            hash = MixFinal(hash);
            return (int)hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Initialize(out uint v1, out uint v2, out uint v3, out uint v4)
        {
            v1 = s_seed + Prime1 + Prime2;
            v2 = s_seed + Prime2;
            v3 = s_seed;
            v4 = s_seed - Prime1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Round(uint hash, uint input)
        {
            return System.Numerics.BitOperations.RotateLeft(hash + input * Prime2, 13) * Prime1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint QueueRound(uint hash, uint queuedValue)
        {
            return System.Numerics.BitOperations.RotateLeft(hash + queuedValue * Prime3, 17) * Prime4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MixState(uint v1, uint v2, uint v3, uint v4)
        {
            return System.Numerics.BitOperations.RotateLeft(v1, 1)
                 + System.Numerics.BitOperations.RotateLeft(v2, 7)
                 + System.Numerics.BitOperations.RotateLeft(v3, 12)
                 + System.Numerics.BitOperations.RotateLeft(v4, 18);
        }

        private static uint MixEmptyState()
        {
            return s_seed + Prime5;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MixFinal(uint hash)
        {
            hash ^= hash >> 15;
            hash *= Prime2;
            hash ^= hash >> 13;
            hash *= Prime3;
            hash ^= hash >> 16;
            return hash;
        }

        public void Add<T>(T value)
        {
            Add(value?.GetHashCode() ?? 0);
        }

        public void Add<T>(T value, IEqualityComparer<T>? comparer)
        {
            Add(value is null ? 0 : (comparer?.GetHashCode(value) ?? value.GetHashCode()));
        }

        private void Add(int value)
        {
            uint val = (uint)value;

            uint previousLength = _length++;
            uint position = previousLength % 4;

            if (position == 0)
                _queue1 = val;
            else if (position == 1)
                _queue2 = val;
            else if (position == 2)
                _queue3 = val;
            else // position == 3
            {
                if (previousLength == 3)
                    Initialize(out _v1, out _v2, out _v3, out _v4);

                _v1 = Round(_v1, _queue1);
                _v2 = Round(_v2, _queue2);
                _v3 = Round(_v3, _queue3);
                _v4 = Round(_v4, val);
            }
        }

        public int ToHashCode()
        {
            uint length = _length;

            uint position = length % 4;

            uint hash = length < 4 ? MixEmptyState() : MixState(_v1, _v2, _v3, _v4);

            hash += length * 4;

            if (position > 0)
            {
                hash = QueueRound(hash, _queue1);
                if (position > 1)
                {
                    hash = QueueRound(hash, _queue2);
                    if (position > 2)
                        hash = QueueRound(hash, _queue3);
                }
            }

            hash = MixFinal(hash);
            return (int)hash;
        }
    }

    public enum DateTimeKind
    {
        Unspecified = 0,
        Utc = 1,
        Local = 2,
    }
    public enum DayOfWeek
    {
        Sunday = 0,
        Monday = 1,
        Tuesday = 2,
        Wednesday = 3,
        Thursday = 4,
        Friday = 5,
        Saturday = 6,
    }
    public readonly struct TimeOnly
    {
        // represent the number of ticks map to the time of the day. 1 ticks = 100-nanosecond in time measurements.
        private readonly ulong _ticks;

        // MinTimeTicks is the ticks for the midnight time 00:00:00.000 AM
        private const long MinTimeTicks = 0;

        // MaxTimeTicks is the max tick value for the time in the day.
        private const long MaxTimeTicks = TimeSpan.TicksPerDay - 1;

        public static TimeOnly MinValue => new TimeOnly((ulong)MinTimeTicks);

        public static TimeOnly MaxValue => new TimeOnly((ulong)MaxTimeTicks);

        public TimeOnly(int hour, int minute) : this(DateTime.TimeToTicks(hour, minute, 0, 0)) { }

        public TimeOnly(int hour, int minute, int second) : this(DateTime.TimeToTicks(hour, minute, second, 0)) { }

        public TimeOnly(int hour, int minute, int second, int millisecond) : this(DateTime.TimeToTicks(hour, minute, second, millisecond)) { }

        public TimeOnly(int hour, int minute, int second, int millisecond, int microsecond) : this(DateTime.TimeToTicks(hour, minute, second, millisecond, microsecond)) { }

        public TimeOnly(long ticks)
        {
            if ((ulong)ticks > MaxTimeTicks)
            {
                throw new ArgumentOutOfRangeException();
            }

            _ticks = (ulong)ticks;
        }

        // exist to bypass the check in the public constructor.
        internal TimeOnly(ulong ticks) => _ticks = ticks;

        public int Hour => (int)(_ticks / TimeSpan.TicksPerHour);

        public int Minute => (int)((uint)(_ticks / TimeSpan.TicksPerMinute) % (uint)TimeSpan.MinutesPerHour);

        public int Second => (int)((uint)(_ticks / TimeSpan.TicksPerSecond) % (uint)TimeSpan.SecondsPerMinute);

        public int Millisecond => (int)((uint)(_ticks / TimeSpan.TicksPerMillisecond) % (uint)TimeSpan.MillisecondsPerSecond);

        public int Microsecond => (int)(_ticks / TimeSpan.TicksPerMicrosecond % (uint)TimeSpan.MicrosecondsPerMillisecond);

        public int Nanosecond => (int)(_ticks % TimeSpan.TicksPerMicrosecond * TimeSpan.NanosecondsPerTick);

        public long Ticks => (long)_ticks;

        private TimeOnly AddTicks(long ticks)
            => new TimeOnly((_ticks + TimeSpan.TicksPerDay + (ulong)(ticks % TimeSpan.TicksPerDay)) % TimeSpan.TicksPerDay);

        private TimeOnly AddTicks(long ticks, out int wrappedDays)
        {
            (long days, long newTicks) = Math.DivRem(ticks, TimeSpan.TicksPerDay);
            newTicks += (long)_ticks;
            if (newTicks < 0)
            {
                days--;
                newTicks += TimeSpan.TicksPerDay;
            }
            else if (newTicks >= TimeSpan.TicksPerDay)
            {
                days++;
                newTicks -= TimeSpan.TicksPerDay;
            }

            wrappedDays = (int)days;
            return new TimeOnly((ulong)newTicks);
        }

        public TimeOnly Add(TimeSpan value) => AddTicks(value.Ticks);

        public TimeOnly Add(TimeSpan value, out int wrappedDays) => AddTicks(value.Ticks, out wrappedDays);

        public TimeOnly AddHours(double value) => AddTicks((long)(value * TimeSpan.TicksPerHour));

        public TimeOnly AddHours(double value, out int wrappedDays) => AddTicks((long)(value * TimeSpan.TicksPerHour), out wrappedDays);

        public TimeOnly AddMinutes(double value) => AddTicks((long)(value * TimeSpan.TicksPerMinute));

        public TimeOnly AddMinutes(double value, out int wrappedDays) => AddTicks((long)(value * TimeSpan.TicksPerMinute), out wrappedDays);

        public bool IsBetween(TimeOnly start, TimeOnly end)
        {
            ulong time = _ticks;
            ulong startTicks = start._ticks;
            ulong endTicks = end._ticks;

            return startTicks <= endTicks
                ? (time - startTicks < endTicks - startTicks)
                : (time - endTicks >= startTicks - endTicks);
        }

        public static bool operator ==(TimeOnly left, TimeOnly right) => left._ticks == right._ticks;

        public static bool operator !=(TimeOnly left, TimeOnly right) => left._ticks != right._ticks;

        public static bool operator >(TimeOnly left, TimeOnly right) => left._ticks > right._ticks;

        public static bool operator >=(TimeOnly left, TimeOnly right) => left._ticks >= right._ticks;

        public static bool operator <(TimeOnly left, TimeOnly right) => left._ticks < right._ticks;

        public static bool operator <=(TimeOnly left, TimeOnly right) => left._ticks <= right._ticks;

        public static TimeSpan operator -(TimeOnly t1, TimeOnly t2)
        {
            long diff = (long)(t1._ticks - t2._ticks);
            // If the result is negative, add 24h to make it positive again using the sign bit.
            return new TimeSpan(diff + ((diff >> 63) & TimeSpan.TicksPerDay));
        }

        public void Deconstruct(out int hour, out int minute)
        {
            hour = Hour;
            minute = Minute;
        }

        public void Deconstruct(out int hour, out int minute, out int second)
        {
            ToDateTime().GetTime(out hour, out minute, out second);
        }

        public void Deconstruct(out int hour, out int minute, out int second, out int millisecond)
        {
            ToDateTime().GetTime(out hour, out minute, out second, out millisecond);
        }


        public static TimeOnly FromTimeSpan(TimeSpan timeSpan) => new TimeOnly(timeSpan._ticks);

        public static TimeOnly FromDateTime(DateTime dateTime) => new TimeOnly((ulong)dateTime.TimeOfDay.Ticks);

        public TimeSpan ToTimeSpan() => new TimeSpan((long)_ticks);

        internal DateTime ToDateTime() => DateTime.CreateUnchecked((long)_ticks);

        public int CompareTo(TimeOnly value) => _ticks.CompareTo(value._ticks);

        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is not TimeOnly timeOnly)
            {
                throw new ArgumentException();
            }

            return CompareTo(timeOnly);
        }

        public bool Equals(TimeOnly value) => _ticks == value._ticks;

        public override bool Equals([NotNullWhen(true)] object? value) => value is TimeOnly timeOnly && _ticks == timeOnly._ticks;

        public override int GetHashCode()
        {
            ulong ticks = _ticks;
            return unchecked((int)ticks) ^ (int)(ticks >> 32);
        }
    }
    public readonly struct DateOnly
    {
        private readonly uint _dayNumber;

        // Maps to Jan 1st year 1
        private const int MinDayNumber = 0;

        // Maps to December 31 year 9999.
        private const int MaxDayNumber = DateTime.DaysTo10000 - 1;

        private static uint DayNumberFromDateTime(DateTime dt) => (uint)((ulong)dt.Ticks / TimeSpan.TicksPerDay);

        internal DateTime GetEquivalentDateTime() => DateTime.CreateUnchecked(_dayNumber * TimeSpan.TicksPerDay);

        private DateOnly(uint dayNumber)
        {
            _dayNumber = dayNumber;
        }

        public static DateOnly MinValue => new DateOnly(MinDayNumber);

        public static DateOnly MaxValue => new DateOnly(MaxDayNumber);

        public DateOnly(int year, int month, int day) => _dayNumber = DayNumberFromDateTime(new DateTime(year, month, day));
        public DateOnly(int year, int month, int day, System.Globalization.Calendar calendar)
            => _dayNumber = DayNumberFromDateTime(new DateTime(year, month, day, calendar));
        public static DateOnly FromDayNumber(int dayNumber)
        {
            if ((uint)dayNumber > MaxDayNumber)
            {
                throw new ArgumentOutOfRangeException();
            }

            return new DateOnly((uint)dayNumber);
        }

        public int Year => GetEquivalentDateTime().Year;

        public int Month => GetEquivalentDateTime().Month;

        public int Day => GetEquivalentDateTime().Day;

        public DayOfWeek DayOfWeek => (DayOfWeek)((_dayNumber + 1) % 7);

        public int DayOfYear => GetEquivalentDateTime().DayOfYear;

        public int DayNumber => (int)_dayNumber;

        public DateOnly AddDays(int value)
        {
            uint newDayNumber = _dayNumber + (uint)value;
            if (newDayNumber > MaxDayNumber)
            {
                throw new ArgumentOutOfRangeException();
            }

            return new DateOnly(newDayNumber);
        }

        public DateOnly AddMonths(int value) => new DateOnly(DayNumberFromDateTime(GetEquivalentDateTime().AddMonths(value)));

        public DateOnly AddYears(int value) => new DateOnly(DayNumberFromDateTime(GetEquivalentDateTime().AddYears(value)));

        public static bool operator ==(DateOnly left, DateOnly right) => left._dayNumber == right._dayNumber;
        public static bool operator !=(DateOnly left, DateOnly right) => left._dayNumber != right._dayNumber;
        public static bool operator >(DateOnly left, DateOnly right) => left._dayNumber > right._dayNumber;
        public static bool operator >=(DateOnly left, DateOnly right) => left._dayNumber >= right._dayNumber;
        public static bool operator <(DateOnly left, DateOnly right) => left._dayNumber < right._dayNumber;
        public static bool operator <=(DateOnly left, DateOnly right) => left._dayNumber <= right._dayNumber;

        public void Deconstruct(out int year, out int month, out int day)
            => GetEquivalentDateTime().GetDate(out year, out month, out day);

        public DateTime ToDateTime(TimeOnly time) => DateTime.CreateUnchecked(_dayNumber * TimeSpan.TicksPerDay + time.Ticks);
        public DateTime ToDateTime(TimeOnly time, DateTimeKind kind) => DateTime.SpecifyKind(ToDateTime(time), kind);
        public static DateOnly FromDateTime(DateTime dateTime) => new DateOnly(DayNumberFromDateTime(dateTime));
        public int CompareTo(DateOnly value) => _dayNumber.CompareTo(value._dayNumber);
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is not DateOnly dateOnly)
            {
                throw new ArgumentException();
            }

            return CompareTo(dateOnly);
        }

        public bool Equals(DateOnly value) => _dayNumber == value._dayNumber;

        public override bool Equals([NotNullWhen(true)] object? value) => value is DateOnly dateOnly && _dayNumber == dateOnly._dayNumber;

        public override int GetHashCode() => (int)_dayNumber;
    }
    public readonly partial struct DateTime
    {
        internal static bool SystemSupportsLeapSeconds => true;
        private static unsafe bool IsValidTimeWithLeapSeconds(DateTime value) => true;

        // Number of days in a non-leap year
        private const int DaysPerYear = 365;
        // Number of days in 4 years
        private const int DaysPer4Years = DaysPerYear * 4 + 1;       // 1461
        // Number of days in 100 years
        private const int DaysPer100Years = DaysPer4Years * 25 - 1;  // 36524
        // Number of days in 400 years
        private const int DaysPer400Years = DaysPer100Years * 4 + 1; // 146097

        // Number of days from 1/1/0001 to 12/31/1600
        private const int DaysTo1601 = DaysPer400Years * 4;          // 584388
        // Number of days from 1/1/0001 to 12/30/1899
        private const int DaysTo1899 = DaysPer400Years * 4 + DaysPer100Years * 3 - 367;
        // Number of days from 1/1/0001 to 12/31/1969
        internal const int DaysTo1970 = DaysPer400Years * 4 + DaysPer100Years * 3 + DaysPer4Years * 17 + DaysPerYear; // 719,162
        // Number of days from 1/1/0001 to 12/31/9999
        internal const int DaysTo10000 = DaysPer400Years * 25 - 366;  // 3652059

        internal const long MinTicks = 0;
        internal const long MaxTicks = DaysTo10000 * TimeSpan.TicksPerDay - 1;
        private const long MaxMicroseconds = MaxTicks / TimeSpan.TicksPerMicrosecond;
        private const long MaxMillis = MaxTicks / TimeSpan.TicksPerMillisecond;
        private const long MaxSeconds = MaxTicks / TimeSpan.TicksPerSecond;
        private const long MaxMinutes = MaxTicks / TimeSpan.TicksPerMinute;
        private const long MaxHours = MaxTicks / TimeSpan.TicksPerHour;
        private const long MaxDays = (long)DaysTo10000 - 1;

        internal const long UnixEpochTicks = DaysTo1970 * TimeSpan.TicksPerDay;
        private const long FileTimeOffset = DaysTo1601 * TimeSpan.TicksPerDay;
        private const long DoubleDateOffset = DaysTo1899 * TimeSpan.TicksPerDay;
        // The minimum OA date is 0100/01/01 (Note it's year 100).
        // The maximum OA date is 9999/12/31
        private const long OADateMinAsTicks = (DaysPer100Years - DaysPerYear) * TimeSpan.TicksPerDay;
        // All OA dates must be greater than (not >=) OADateMinAsDouble
        private const double OADateMinAsDouble = -657435.0;
        // All OA dates must be less than (not <=) OADateMaxAsDouble
        private const double OADateMaxAsDouble = 2958466.0;

        // Euclidean Affine Functions Algorithm (EAF) constants

        // Constants used for fast calculation of following subexpressions
        //      x / DaysPer4Years
        //      x % DaysPer4Years / 4
        private const uint EafMultiplier = (uint)(((1UL << 32) + DaysPer4Years - 1) / DaysPer4Years);   // 2,939,745
        private const uint EafDivider = EafMultiplier * 4;                                              // 11,758,980

        private const ulong TicksPer6Hours = TimeSpan.TicksPerHour * 6;
        private const int March1BasedDayOfNewYear = 306;              // Days between March 1 and January 1

        internal static ReadOnlySpan<uint> DaysToMonth365 => [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365];
        internal static ReadOnlySpan<uint> DaysToMonth366 => [0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366];

        private static ReadOnlySpan<byte> DaysInMonth365 => [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        private static ReadOnlySpan<byte> DaysInMonth366 => [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

        public static readonly DateTime MinValue;
        public static readonly DateTime MaxValue = new DateTime(MaxTicks, DateTimeKind.Unspecified);
        public static readonly DateTime UnixEpoch = new DateTime(UnixEpochTicks, DateTimeKind.Utc);

        private const ulong TicksMask = 0x3FFFFFFFFFFFFFFF;
        private const ulong FlagsMask = 0xC000000000000000;
        private const long TicksCeiling = 0x4000000000000000;
        internal const ulong KindUtc = 0x4000000000000000;
        private const ulong KindLocal = 0x8000000000000000;
        private const ulong KindLocalAmbiguousDst = 0xC000000000000000;
        private const int KindShift = 62;

        private const string TicksField = "ticks"; // Do not rename (binary serialization)
        private const string DateDataField = "dateData"; // Do not rename (binary serialization)

        internal readonly ulong _dateData;

        public DateTime(long ticks)
        {
            if ((ulong)ticks > MaxTicks) ThrowTicksOutOfRange();
            _dateData = (ulong)ticks;
        }

        private DateTime(ulong dateData)
        {
            _dateData = dateData;
        }

        internal static DateTime CreateUnchecked(long ticks) => new DateTime((ulong)ticks);

        public DateTime(long ticks, DateTimeKind kind)
        {
            if ((ulong)ticks > MaxTicks) ThrowTicksOutOfRange();
            if ((uint)kind > (uint)DateTimeKind.Local) ThrowInvalidKind();
            _dateData = (ulong)ticks | ((ulong)(uint)kind << KindShift);
        }
        public DateTime(DateOnly date, TimeOnly time)
        {
            _dateData = (ulong)(date.DayNumber * TimeSpan.TicksPerDay + time.Ticks);
        }
        public DateTime(DateOnly date, TimeOnly time, DateTimeKind kind)
        {
            if ((uint)kind > (uint)DateTimeKind.Local) ThrowInvalidKind();
            _dateData = (ulong)(date.DayNumber * TimeSpan.TicksPerDay + time.Ticks) | ((ulong)(uint)kind << KindShift);
        }

        internal DateTime(long ticks, DateTimeKind kind, bool isAmbiguousDst)
        {
            if ((ulong)ticks > MaxTicks) ThrowTicksOutOfRange();
            _dateData = ((ulong)ticks | (isAmbiguousDst ? KindLocalAmbiguousDst : KindLocal));
        }

        private static void ThrowTicksOutOfRange() => throw new ArgumentOutOfRangeException("ticks");
        private static void ThrowInvalidKind() => throw new ArgumentException("kind");
        internal static void ThrowMillisecondOutOfRange() => throw new ArgumentOutOfRangeException("millisecond");
        internal static void ThrowMicrosecondOutOfRange() => throw new ArgumentOutOfRangeException("microsecond");
        private static void ThrowDateArithmetic(int param) => throw new ArgumentOutOfRangeException();
        private static void ThrowAddOutOfRange() => throw new ArgumentOutOfRangeException("value");

        public DateTime(int year, int month, int day)
        {
            _dateData = DateToTicks(year, month, day);
        }

        public DateTime(int year, int month, int day, System.Globalization.Calendar calendar)
            : this(year, month, day, 0, 0, 0, calendar)
        {
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, System.Globalization.Calendar calendar, DateTimeKind kind)
        {
            if (calendar == null) throw new ArgumentNullException();

            if ((uint)millisecond >= TimeSpan.MillisecondsPerSecond) ThrowMillisecondOutOfRange();
            if ((uint)kind > (uint)DateTimeKind.Local) ThrowInvalidKind();

            if (second != 60 || !SystemSupportsLeapSeconds)
            {
                ulong ticks = calendar.ToDateTime(year, month, day, hour, minute, second, millisecond).UTicks;
                _dateData = ticks | ((ulong)(uint)kind << KindShift);
            }
            else
            {
                _dateData = WithLeapSecond(calendar, year, month, day, hour, minute, millisecond, kind);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ulong WithLeapSecond(
            System.Globalization.Calendar calendar, int year, int month, int day, int hour, int minute, int millisecond, DateTimeKind kind)
        {
            // if we have a leap second, then we adjust it to 59 so that DateTime will consider it the last in the specified minute.
            return ValidateLeapSecond(new DateTime(year, month, day, hour, minute, 59, millisecond, calendar, kind));
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second)
        {
            ulong ticks = DateToTicks(year, month, day);
            if (second != 60 || !SystemSupportsLeapSeconds)
            {
                _dateData = ticks + TimeToTicks(hour, minute, second);
            }
            else
            {
                _dateData = WithLeapSecond(ticks, hour, minute);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ulong WithLeapSecond(ulong ticks, int hour, int minute)
        {
            // if we have a leap second, then we adjust it to 59 so that DateTime will consider it the last in the specified minute.
            return ValidateLeapSecond(new DateTime(ticks + TimeToTicks(hour, minute, 59)));
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, DateTimeKind kind)
        {
            if ((uint)kind > (uint)DateTimeKind.Local) ThrowInvalidKind();

            ulong ticks = DateToTicks(year, month, day) | ((ulong)(uint)kind << KindShift);
            if (second != 60 || !SystemSupportsLeapSeconds)
            {
                _dateData = ticks + TimeToTicks(hour, minute, second);
            }
            else
            {
                _dateData = WithLeapSecond(ticks, hour, minute);
            }
        }

        // Constructs a DateTime from a given year, month, day, hour,
        // minute, and second for the specified calendar.
        //
        public DateTime(int year, int month, int day, int hour, int minute, int second, System.Globalization.Calendar calendar)
        {
            if (calendar == null) throw new ArgumentNullException();

            if (second != 60 || !SystemSupportsLeapSeconds)
            {
                _dateData = calendar.ToDateTime(year, month, day, hour, minute, second, 0).UTicks;
            }
            else
            {
                _dateData = WithLeapSecond(calendar, year, month, day, hour, minute);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ulong WithLeapSecond(System.Globalization.Calendar calendar, int year, int month, int day, int hour, int minute)
        {
            // if we have a leap second, then we adjust it to 59 so that DateTime will consider it the last in the specified minute.
            return ValidateLeapSecond(new DateTime(year, month, day, hour, minute, 59, calendar));
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond)
            : this(year, month, day, hour, minute, second)
        {
            if ((uint)millisecond >= TimeSpan.MillisecondsPerSecond) ThrowMillisecondOutOfRange();
            _dateData += (uint)millisecond * (uint)TimeSpan.TicksPerMillisecond;
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, DateTimeKind kind)
            : this(year, month, day, hour, minute, second, kind)
        {
            if ((uint)millisecond >= TimeSpan.MillisecondsPerSecond) ThrowMillisecondOutOfRange();
            _dateData += (uint)millisecond * (uint)TimeSpan.TicksPerMillisecond;
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, System.Globalization.Calendar calendar)
        {
            if (calendar == null) throw new ArgumentNullException();

            if (second != 60 || !SystemSupportsLeapSeconds)
            {
                _dateData = calendar.ToDateTime(year, month, day, hour, minute, second, millisecond).UTicks;
            }
            else
            {
                _dateData = WithLeapSecond(calendar, year, month, day, hour, minute, millisecond);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ulong WithLeapSecond(System.Globalization.Calendar calendar,
            int year, int month, int day, int hour, int minute, int millisecond)
        {
            // if we have a leap second, then we adjust it to 59 so that DateTime will consider it the last in the specified minute.
            return ValidateLeapSecond(new DateTime(year, month, day, hour, minute, 59, millisecond, calendar));
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, int microsecond)
            : this(year, month, day, hour, minute, second, millisecond, microsecond, DateTimeKind.Unspecified)
        {
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, int microsecond, DateTimeKind kind)
            : this(year, month, day, hour, minute, second, millisecond, kind)
        {
            if ((uint)microsecond >= TimeSpan.MicrosecondsPerMillisecond) ThrowMicrosecondOutOfRange();
            _dateData += (uint)microsecond * (uint)TimeSpan.TicksPerMicrosecond;
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second,
            int millisecond, int microsecond, System.Globalization.Calendar calendar)
           : this(year, month, day, hour, minute, second, millisecond, microsecond, calendar, DateTimeKind.Unspecified)
        {
        }

        public DateTime(int year, int month, int day, int hour, int minute, int second,
            int millisecond, int microsecond, System.Globalization.Calendar calendar, DateTimeKind kind)
            : this(year, month, day, hour, minute, second, millisecond, calendar, kind)
        {
            if ((uint)microsecond >= TimeSpan.MicrosecondsPerMillisecond) ThrowMicrosecondOutOfRange();
            _dateData += (uint)microsecond * (uint)TimeSpan.TicksPerMicrosecond;
        }

        internal static ulong ValidateLeapSecond(DateTime value)
        {
            if (!IsValidTimeWithLeapSeconds(value))
            {
                throw new ArgumentOutOfRangeException();
            }
            return value._dateData;
        }

        private ulong UTicks => _dateData & TicksMask;

        private ulong InternalKind => _dateData & FlagsMask;

        // Returns the DateTime resulting from adding the given
        // TimeSpan to this DateTime.
        //
        public DateTime Add(TimeSpan value)
        {
            return AddTicks(value._ticks);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private DateTime AddUnits(double value, long maxUnitCount, long ticksPerUnit)
        {
            if (Math.Abs(value) > maxUnitCount)
            {
                ThrowAddOutOfRange();
            }

            double integralPart = Math.Truncate(value);
            double fractionalPart = value - integralPart;
            long ticks = (long)(integralPart) * ticksPerUnit;
            ticks += (long)(fractionalPart * ticksPerUnit);

            return AddTicks(ticks);
        }

        public DateTime AddDays(double value) => AddUnits(value, MaxDays, TimeSpan.TicksPerDay);

        public DateTime AddHours(double value) => AddUnits(value, MaxHours, TimeSpan.TicksPerHour);

        public DateTime AddMilliseconds(double value) => AddUnits(value, MaxMillis, TimeSpan.TicksPerMillisecond);

        public DateTime AddMicroseconds(double value) => AddUnits(value, MaxMicroseconds, TimeSpan.TicksPerMicrosecond);

        public DateTime AddMinutes(double value) => AddUnits(value, MaxMinutes, TimeSpan.TicksPerMinute);

        public DateTime AddMonths(int months) => AddMonths(this, months);
        private static DateTime AddMonths(DateTime date, int months)
        {
            if (months < -120000 || months > 120000) throw new ArgumentOutOfRangeException();
            date.GetDate(out int year, out int month, out int day);
            int y = year, d = day;
            int m = month + months;
            int q = m > 0 ? (int)((uint)(m - 1) / 12) : m / 12 - 1;
            y += q;
            m -= q * 12;
            if (y < 1 || y > 9999) ThrowDateArithmetic(2);
            ReadOnlySpan<uint> daysTo = IsLeapYear(y) ? DaysToMonth366 : DaysToMonth365;
            uint daysToMonth = daysTo[m - 1];
            int days = (int)(daysTo[m] - daysToMonth);
            if (d > days) d = days;
            uint n = DaysToYear((uint)y) + daysToMonth + (uint)d - 1;
            return new DateTime(n * (ulong)TimeSpan.TicksPerDay + date.UTicks % TimeSpan.TicksPerDay | date.InternalKind);
        }

        public DateTime AddSeconds(double value) => AddUnits(value, MaxSeconds, TimeSpan.TicksPerSecond);

        // Returns the DateTime resulting from adding the given number of
        // 100-nanosecond ticks to this DateTime. The value argument
        // is permitted to be negative.
        //
        public DateTime AddTicks(long value)
        {
            ulong ticks = (ulong)(Ticks + value);
            if (ticks > MaxTicks) ThrowDateArithmetic(0);
            return new DateTime(ticks | InternalKind);
        }

        internal bool TryAddTicks(long value, out DateTime result)
        {
            ulong ticks = (ulong)(Ticks + value);
            if (ticks > MaxTicks)
            {
                result = default;
                return false;
            }
            result = new DateTime(ticks | InternalKind);
            return true;
        }

        public DateTime AddYears(int value) => AddYears(this, value);
        private static DateTime AddYears(DateTime date, int value)
        {
            if (value < -10000 || value > 10000)
            {
                throw new ArgumentOutOfRangeException();
            }
            date.GetDate(out int year, out int month, out int day);
            int y = year + value;
            if (y < 1 || y > 9999) ThrowDateArithmetic(0);
            uint n = DaysToYear((uint)y);

            int m = month - 1, d = day - 1;
            if (IsLeapYear(y))
            {
                n += DaysToMonth366[m];
            }
            else
            {
                if (d == 28 && m == 1) d--;
                n += DaysToMonth365[m];
            }
            n += (uint)d;
            return new DateTime(n * (ulong)TimeSpan.TicksPerDay + date.UTicks % TimeSpan.TicksPerDay | date.InternalKind);
        }

        public static int Compare(DateTime t1, DateTime t2)
        {
            long ticks1 = t1.Ticks;
            long ticks2 = t2.Ticks;
            if (ticks1 > ticks2) return 1;
            if (ticks1 < ticks2) return -1;
            return 0;
        }
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (!(value is DateTime))
            {
                throw new ArgumentException();
            }

            return Compare(this, (DateTime)value);
        }

        public int CompareTo(DateTime value)
        {
            return Compare(this, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong DateToTicks(int year, int month, int day)
        {
            if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1)
            {
                throw new ArgumentOutOfRangeException();
            }

            ReadOnlySpan<uint> days = RuntimeHelpers.IsKnownConstant(month) && month == 1 || IsLeapYear(year) ? DaysToMonth366 : DaysToMonth365;
            if ((uint)day > days[month] - days[month - 1])
            {
                throw new ArgumentOutOfRangeException();
            }

            uint n = DaysToYear((uint)year) + days[month - 1] + (uint)day - 1;
            return n * (ulong)TimeSpan.TicksPerDay;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint DaysToYear(uint year)
        {
            uint y = year - 1;
            uint cent = y / 100;
            return y * (365 * 4 + 1) / 4 - cent + cent / 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong TimeToTicks(int hour, int minute, int second)
        {
            if ((uint)hour >= 24 || (uint)minute >= 60 || (uint)second >= 60)
            {
                throw new ArgumentOutOfRangeException();
            }

            int totalSeconds = hour * 3600 + minute * 60 + second;
            return (uint)totalSeconds * (ulong)TimeSpan.TicksPerSecond;
        }

        internal static ulong TimeToTicks(int hour, int minute, int second, int millisecond)
        {
            ulong ticks = TimeToTicks(hour, minute, second);

            if ((uint)millisecond >= TimeSpan.MillisecondsPerSecond) ThrowMillisecondOutOfRange();

            ticks += (uint)millisecond * (uint)TimeSpan.TicksPerMillisecond;

            return ticks;
        }

        internal static ulong TimeToTicks(int hour, int minute, int second, int millisecond, int microsecond)
        {
            ulong ticks = TimeToTicks(hour, minute, second, millisecond);

            if ((uint)microsecond >= TimeSpan.MicrosecondsPerMillisecond) ThrowMicrosecondOutOfRange();

            ticks += (uint)microsecond * (uint)TimeSpan.TicksPerMicrosecond;

            return ticks;
        }

        public static DateTime SpecifyKind(DateTime value, DateTimeKind kind)
        {
            if ((uint)kind > (uint)DateTimeKind.Local) ThrowInvalidKind();
            return new DateTime(value.UTicks | ((ulong)(uint)kind << KindShift));
        }

        public DateTime Date => new((UTicks / TimeSpan.TicksPerDay * TimeSpan.TicksPerDay) | InternalKind);

        internal void GetDate(out int year, out int month, out int day) => GetDate(_dateData, out year, out month, out day);
        private static void GetDate(ulong dateData, out int year, out int month, out int day)
        {
            // y100 = number of whole 100-year periods since 3/1/0000
            // r1 = (day number within 100-year period) * 4
            (uint y100, uint r1) = Math.DivRem(((uint)((dateData & TicksMask) / TicksPer6Hours) | 3U) + 1224, DaysPer400Years);
            ulong u2 = Math.BigMul(EafMultiplier, r1 | 3U);
            uint daySinceMarch1 = (uint)u2 / EafDivider;
            uint n3 = 2141 * daySinceMarch1 + 197913;
            year = (int)(100 * y100 + (uint)(u2 >> 32));
            // compute month and day
            month = (int)(n3 >> 16);
            day = (ushort)n3 / 2141 + 1;

            // rollover December 31
            if (daySinceMarch1 >= March1BasedDayOfNewYear)
            {
                ++year;
                month -= 12;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void GetTime(out int hour, out int minute, out int second)
        {
            ulong seconds = UTicks / TimeSpan.TicksPerSecond;
            ulong minutes = seconds / 60;
            second = (int)(seconds - (minutes * 60));
            ulong hours = minutes / 60;
            minute = (int)(minutes - (hours * 60));
            hour = (int)((uint)hours % 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void GetTime(out int hour, out int minute, out int second, out int millisecond)
        {
            ulong milliseconds = UTicks / TimeSpan.TicksPerMillisecond;
            ulong seconds = milliseconds / 1000;
            millisecond = (int)(milliseconds - (seconds * 1000));
            ulong minutes = seconds / 60;
            second = (int)(seconds - (minutes * 60));
            ulong hours = minutes / 60;
            minute = (int)(minutes - (hours * 60));
            hour = (int)((uint)hours % 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void GetTimePrecise(out int hour, out int minute, out int second, out int tick)
        {
            ulong ticks = UTicks;
            ulong seconds = ticks / TimeSpan.TicksPerSecond;
            tick = (int)(ticks - (seconds * TimeSpan.TicksPerSecond));
            ulong minutes = seconds / 60;
            second = (int)(seconds - (minutes * 60));
            ulong hours = minutes / 60;
            minute = (int)(minutes - (hours * 60));
            hour = (int)((uint)hours % 24);
        }

        public int Day
        {
            get
            {
                // r1 = (day number within 100-year period) * 4
                uint r1 = (((uint)(UTicks / TicksPer6Hours) | 3U) + 1224) % DaysPer400Years;
                ulong u2 = Math.BigMul(EafMultiplier, r1 | 3U);
                ushort daySinceMarch1 = (ushort)((uint)u2 / EafDivider);
                int n3 = 2141 * daySinceMarch1 + 197913;
                // Return 1-based day-of-month
                return (ushort)n3 / 2141 + 1;
            }
        }

        public DayOfWeek DayOfWeek => (DayOfWeek)(((uint)(UTicks / TimeSpan.TicksPerDay) + 1) % 7);

        // Returns the day-of-year part of this DateTime. The returned value
        // is an integer between 1 and 366.
        //
        public int DayOfYear =>
            1 + (int)(((((uint)(UTicks / TicksPer6Hours) | 3U) % (uint)DaysPer400Years) | 3U) * EafMultiplier / EafDivider);

        // Returns the hash code for this DateTime.
        //
        public override int GetHashCode()
        {
            long ticks = Ticks;
            return unchecked((int)ticks) ^ (int)(ticks >> 32);
        }

        // Returns the hour part of this DateTime. The returned value is an
        // integer between 0 and 23.
        //
        public int Hour => (int)((uint)(UTicks / TimeSpan.TicksPerHour) % 24);

        internal bool IsAmbiguousDaylightSavingTime() => _dateData >= KindLocalAmbiguousDst;

        public DateTimeKind Kind
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                uint kind = (uint)(_dateData >> KindShift);
                // values 0-2 map directly to DateTimeKind, 3 (LocalAmbiguousDst) needs to be mapped to 2 (Local) using bit0 NAND bit1
                return (DateTimeKind)(kind & ~(kind >> 1));
            }
        }

        // Returns the millisecond part of this DateTime. The returned value
        // is an integer between 0 and 999.
        //
        public int Millisecond => (int)((UTicks / TimeSpan.TicksPerMillisecond) % 1000);

        /// <summary>
        /// The microseconds component, expressed as a value between 0 and 999.
        /// </summary>
        public int Microsecond => (int)((UTicks / TimeSpan.TicksPerMicrosecond) % 1000);

        /// <summary>
        /// The nanoseconds component, expressed as a value between 0 and 900 (in increments of 100 nanoseconds).
        /// </summary>
        public int Nanosecond => (int)(UTicks % TimeSpan.TicksPerMicrosecond) * 100;

        // Returns the minute part of this DateTime. The returned value is
        // an integer between 0 and 59.
        //
        public int Minute => (int)((UTicks / TimeSpan.TicksPerMinute) % 60);

        // Returns the month part of this DateTime. The returned value is an
        // integer between 1 and 12.
        //
        public int Month
        {
            get
            {
                // r1 = (day number within 100-year period) * 4
                uint r1 = (((uint)(UTicks / TicksPer6Hours) | 3U) + 1224) % DaysPer400Years;
                ulong u2 = Math.BigMul(EafMultiplier, r1 | 3U);
                ushort daySinceMarch1 = (ushort)((uint)u2 / EafDivider);
                int n3 = 2141 * daySinceMarch1 + 197913;
                return (ushort)(n3 >> 16) - (daySinceMarch1 >= March1BasedDayOfNewYear ? 12 : 0);
            }
        }

        // Returns the second part of this DateTime. The returned value is
        // an integer between 0 and 59.
        //
        public int Second => (int)((UTicks / TimeSpan.TicksPerSecond) % 60);

        // Returns the tick count for this DateTime. The returned value is
        // the number of 100-nanosecond intervals that have elapsed since 1/1/0001
        // 12:00am.
        //
        public long Ticks => (long)(_dateData & TicksMask);

        // Returns the time-of-day part of this DateTime. The returned value
        // is a TimeSpan that indicates the time elapsed since midnight.
        //
        public TimeSpan TimeOfDay => new TimeSpan((long)(UTicks % TimeSpan.TicksPerDay));

        public int Year => GetYear(_dateData);
        private static int GetYear(ulong dateData)
        {
            // y100 = number of whole 100-year periods since 1/1/0001
            // r1 = (day number within 100-year period) * 4
            (uint y100, uint r1) = Math.DivRem(((uint)((dateData & TicksMask) / TicksPer6Hours) | 3U), DaysPer400Years);
            return 1 + (int)(100 * y100 + (r1 | 3) / DaysPer4Years);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLeapYear(int year)
        {
            if (year < 1 || year > 9999)
            {
                throw new ArgumentOutOfRangeException();
            }
            if ((year & 3) != 0) return false;
            if ((year & 15) == 0) return true;
            return (uint)year % 25 != 0;
        }

        public TimeSpan Subtract(DateTime value)
        {
            return new TimeSpan(Ticks - value.Ticks);
        }

        public DateTime Subtract(TimeSpan value)
        {
            ulong ticks = (ulong)(Ticks - value._ticks);
            if (ticks > MaxTicks) ThrowDateArithmetic(0);
            return new DateTime(ticks | InternalKind);
        }

        private static double TicksToOADate(long value)
        {
            if (value == 0)
                return 0.0;  // Returns OleAut's zero'ed date value.
            if (value < TimeSpan.TicksPerDay) // This is a fix for VB. They want the default day to be 1/1/0001 rather than 12/30/1899.
                value += DoubleDateOffset; // We could have moved this fix down but we would like to keep the bounds check.
            if (value < OADateMinAsTicks)
                throw new OverflowException();
            // Currently, our max date == OA's max date (12/31/9999), so we don't
            // need an overflow check in that direction.
            long millis = (value - DoubleDateOffset) / TimeSpan.TicksPerMillisecond;
            if (millis < 0)
            {
                long frac = millis % TimeSpan.MillisecondsPerDay;
                if (frac != 0) millis -= (TimeSpan.MillisecondsPerDay + frac) * 2;
            }
            return (double)millis / TimeSpan.MillisecondsPerDay;
        }

        // Converts the DateTime instance into an OLE Automation compatible
        // double date.
        public double ToOADate()
        {
            return TicksToOADate(Ticks);
        }

        public static DateTime operator +(DateTime d, TimeSpan t)
        {
            ulong ticks = (ulong)(d.Ticks + t._ticks);
            if (ticks > MaxTicks) ThrowDateArithmetic(1);
            return new DateTime(ticks | d.InternalKind);
        }

        public static DateTime operator -(DateTime d, TimeSpan t)
        {
            ulong ticks = (ulong)(d.Ticks - t._ticks);
            if (ticks > MaxTicks) ThrowDateArithmetic(1);
            return new DateTime(ticks | d.InternalKind);
        }

        public static TimeSpan operator -(DateTime d1, DateTime d2) => new TimeSpan(d1.Ticks - d2.Ticks);

        public static bool operator ==(DateTime d1, DateTime d2) => ((d1._dateData ^ d2._dateData) << 2) == 0;

        public static bool operator !=(DateTime d1, DateTime d2) => !(d1 == d2);

        public static bool operator <(DateTime t1, DateTime t2) => t1.Ticks < t2.Ticks;

        public static bool operator <=(DateTime t1, DateTime t2) => t1.Ticks <= t2.Ticks;

        public static bool operator >(DateTime t1, DateTime t2) => t1.Ticks > t2.Ticks;

        public static bool operator >=(DateTime t1, DateTime t2) => t1.Ticks >= t2.Ticks;

        public void Deconstruct(out DateOnly date, out TimeOnly time)
        {
            date = DateOnly.FromDateTime(this);
            time = TimeOnly.FromDateTime(this);
        }

        public void Deconstruct(out int year, out int month, out int day)
        {
            GetDate(out year, out month, out day);
        }

        public TypeCode GetTypeCode() => TypeCode.DateTime;
    }
    public readonly struct TimeSpan
    {
        public const long NanosecondsPerTick = 100;                                                 //             100

        public const long TicksPerMicrosecond = 10;                                                 //              10

        public const long TicksPerMillisecond = TicksPerMicrosecond * 1000;                         //          10,000

        public const long TicksPerSecond = TicksPerMillisecond * 1000;                              //      10,000,000

        public const long TicksPerMinute = TicksPerSecond * 60;                                     //     600,000,000

        public const long TicksPerHour = TicksPerMinute * 60;                                       //  36,000,000,000

        public const long TicksPerDay = TicksPerHour * 24;                                          // 864,000,000,000

        public const long MicrosecondsPerMillisecond = TicksPerMillisecond / TicksPerMicrosecond;   //           1,000

        public const long MicrosecondsPerSecond = TicksPerSecond / TicksPerMicrosecond;             //       1,000,000

        public const long MicrosecondsPerMinute = TicksPerMinute / TicksPerMicrosecond;             //      60,000,000

        public const long MicrosecondsPerHour = TicksPerHour / TicksPerMicrosecond;                 //   3,600,000,000

        public const long MicrosecondsPerDay = TicksPerDay / TicksPerMicrosecond;                   //  86,400,000,000

        public const long MillisecondsPerSecond = TicksPerSecond / TicksPerMillisecond;             //           1,000

        public const long MillisecondsPerMinute = TicksPerMinute / TicksPerMillisecond;             //          60,000

        public const long MillisecondsPerHour = TicksPerHour / TicksPerMillisecond;                 //       3,600,000

        public const long MillisecondsPerDay = TicksPerDay / TicksPerMillisecond;                   //      86,400,000

        public const long SecondsPerMinute = TicksPerMinute / TicksPerSecond;                       //              60

        public const long SecondsPerHour = TicksPerHour / TicksPerSecond;                           //           3,600

        public const long SecondsPerDay = TicksPerDay / TicksPerSecond;                             //          86,400

        public const long MinutesPerHour = TicksPerHour / TicksPerMinute;                           //              60

        public const long MinutesPerDay = TicksPerDay / TicksPerMinute;                             //           1,440

        public const int HoursPerDay = (int)(TicksPerDay / TicksPerHour);                           //              24

        internal const long MinTicks = long.MinValue;                                               // -9,223,372,036,854,775,808
        internal const long MaxTicks = long.MaxValue;                                               // +9,223,372,036,854,775,807

        internal const long MinMicroseconds = MinTicks / TicksPerMicrosecond;                       // -  922,337,203,685,477,580
        internal const long MaxMicroseconds = MaxTicks / TicksPerMicrosecond;                       // +  922,337,203,685,477,580

        internal const long MinMilliseconds = MinTicks / TicksPerMillisecond;                       // -      922,337,203,685,477
        internal const long MaxMilliseconds = MaxTicks / TicksPerMillisecond;                       // +      922,337,203,685,477

        internal const long MinSeconds = MinTicks / TicksPerSecond;                                 // -          922,337,203,685
        internal const long MaxSeconds = MaxTicks / TicksPerSecond;                                 // +          922,337,203,685

        internal const long MinMinutes = MinTicks / TicksPerMinute;                                 // -           15,372,286,728
        internal const long MaxMinutes = MaxTicks / TicksPerMinute;                                 // +           15,372,286,728

        internal const long MinHours = MinTicks / TicksPerHour;                                     // -              256,204,778
        internal const long MaxHours = MaxTicks / TicksPerHour;                                     // +              256,204,778

        internal const long MinDays = MinTicks / TicksPerDay;                                       // -               10,675,199
        internal const long MaxDays = MaxTicks / TicksPerDay;                                       // +               10,675,199

        internal const long TicksPerTenthSecond = TicksPerMillisecond * 100;

        public static readonly TimeSpan Zero = new TimeSpan(0);

        public static readonly TimeSpan MaxValue = new TimeSpan(MaxTicks);
        public static readonly TimeSpan MinValue = new TimeSpan(MinTicks);

        internal readonly long _ticks; // Do not rename

        public TimeSpan(long ticks)
        {
            _ticks = ticks;
        }
        public TimeSpan(int hours, int minutes, int seconds)
        {
            _ticks = TimeToTicks(hours, minutes, seconds);
        }

        public TimeSpan(int days, int hours, int minutes, int seconds)
            : this(days, hours, minutes, seconds, 0)
        {
        }
        public TimeSpan(int days, int hours, int minutes, int seconds, int milliseconds) :
            this(days, hours, minutes, seconds, milliseconds, 0)
        {
        }
        public TimeSpan(int days, int hours, int minutes, int seconds, int milliseconds, int microseconds)
        {
            long totalMicroseconds = (days * MicrosecondsPerDay)
                                   + (hours * MicrosecondsPerHour)
                                   + (minutes * MicrosecondsPerMinute)
                                   + (seconds * MicrosecondsPerSecond)
                                   + (milliseconds * MicrosecondsPerMillisecond)
                                   + microseconds;

            if ((totalMicroseconds > MaxMicroseconds) || (totalMicroseconds < MinMicroseconds))
            {
                throw new ArgumentOutOfRangeException();
            }
            _ticks = totalMicroseconds * TicksPerMicrosecond;
        }

        public long Ticks => _ticks;

        public int Days => (int)(_ticks / TicksPerDay);

        public int Hours => (int)(_ticks / TicksPerHour % HoursPerDay);

        public int Milliseconds => (int)(_ticks / TicksPerMillisecond % MillisecondsPerSecond);

        public int Microseconds => (int)(_ticks / TicksPerMicrosecond % MicrosecondsPerMillisecond);

        public int Nanoseconds => (int)(_ticks % TicksPerMicrosecond * NanosecondsPerTick);

        public int Minutes => (int)(_ticks / TicksPerMinute % MinutesPerHour);

        public int Seconds => (int)(_ticks / TicksPerSecond % SecondsPerMinute);

        public double TotalDays => (double)_ticks / TicksPerDay;

        public double TotalHours => (double)_ticks / TicksPerHour;

        public double TotalMilliseconds
        {
            get
            {
                double temp = (double)_ticks / TicksPerMillisecond;

                if (temp > MaxMilliseconds)
                {
                    return MaxMilliseconds;
                }

                if (temp < MinMilliseconds)
                {
                    return MinMilliseconds;
                }
                return temp;
            }
        }
        public double TotalMicroseconds => (double)_ticks / TicksPerMicrosecond;

        public double TotalNanoseconds => (double)_ticks * NanosecondsPerTick;

        public double TotalMinutes => (double)_ticks / TicksPerMinute;

        public double TotalSeconds => (double)_ticks / TicksPerSecond;

        public TimeSpan Add(TimeSpan ts) => this + ts;
        public static int Compare(TimeSpan t1, TimeSpan t2) => t1._ticks.CompareTo(t2._ticks);
        public int CompareTo(object? value)
        {
            if (value is null)
            {
                return 1;
            }

            if (value is TimeSpan other)
            {
                return CompareTo(other);
            }

            throw new ArgumentException();
        }
        public int CompareTo(TimeSpan value) => Compare(this, value);

        public static TimeSpan FromDays(double value) => Interval(value, TicksPerDay);

        public TimeSpan Duration()
        {
            if (_ticks == MinTicks)
            {
                throw new OverflowException();
            }
            return new TimeSpan(_ticks >= 0 ? _ticks : -_ticks);
        }

        public override bool Equals([NotNullWhen(true)] object? value) => (value is TimeSpan other) && Equals(other);

        public bool Equals(TimeSpan obj) => Equals(this, obj);

        public static bool Equals(TimeSpan t1, TimeSpan t2) => t1 == t2;

        public override int GetHashCode() => _ticks.GetHashCode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TimeSpan FromUnits(long units, long ticksPerUnit, long minUnits, long maxUnits)
        {
            if (units > maxUnits || units < minUnits)
            {
                throw new ArgumentOutOfRangeException();
            }
            return TimeSpan.FromTicks(units * ticksPerUnit);
        }

        public static TimeSpan FromDays(int days) => FromUnits(days, TicksPerDay, MinDays, MaxDays);

        public static TimeSpan FromHours(int hours) => FromUnits(hours, TicksPerHour, MinHours, MaxHours);

        public static TimeSpan FromMinutes(long minutes) => FromUnits(minutes, TicksPerMinute, MinMinutes, MaxMinutes);

        public static TimeSpan FromSeconds(long seconds) => FromUnits(seconds, TicksPerSecond, MinSeconds, MaxSeconds);

        public static TimeSpan FromMilliseconds(long milliseconds)
            => FromUnits(milliseconds, TicksPerMillisecond, MinMilliseconds, MaxMilliseconds);

        public static TimeSpan FromMicroseconds(long microseconds) => FromUnits(microseconds, TicksPerMicrosecond, MinMicroseconds, MaxMicroseconds);

        public static TimeSpan FromHours(double value) => Interval(value, TicksPerHour);

        private static TimeSpan Interval(double value, double scale)
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentException();
            }
            return IntervalFromDoubleTicks(value * scale);
        }

        private static TimeSpan IntervalFromDoubleTicks(double ticks)
        {
            if ((ticks > MaxTicks) || (ticks < MinTicks) || double.IsNaN(ticks))
            {
                throw new OverflowException();
            }
            if (ticks == MaxTicks)
            {
                return MaxValue;
            }
            return new TimeSpan((long)ticks);
        }
        public static TimeSpan FromMilliseconds(double value) => Interval(value, TicksPerMillisecond);

        public static TimeSpan FromMicroseconds(double value) => Interval(value, TicksPerMicrosecond);

        public static TimeSpan FromMinutes(double value) => Interval(value, TicksPerMinute);

        public TimeSpan Negate() => -this;

        public static TimeSpan FromSeconds(double value) => Interval(value, TicksPerSecond);

        public TimeSpan Subtract(TimeSpan ts) => this - ts;

        public TimeSpan Multiply(double factor) => this * factor;

        public TimeSpan Divide(double divisor) => this / divisor;

        public double Divide(TimeSpan ts) => this / ts;

        public static TimeSpan FromTicks(long value) => new TimeSpan(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long TimeToTicks(int hour, int minute, int second)
        {
            // totalSeconds is bounded by 2^31 * 2^12 + 2^31 * 2^8 + 2^31,
            // which is less than 2^44, meaning we won't overflow totalSeconds.
            long totalSeconds = (hour * SecondsPerHour)
                              + (minute * SecondsPerMinute)
                              + second;

            if ((totalSeconds > MaxSeconds) || (totalSeconds < MinSeconds))
            {
                throw new ArgumentOutOfRangeException();
            }
            return totalSeconds * TicksPerSecond;
        }

        public static TimeSpan operator -(TimeSpan t)
        {
            if (t._ticks == MinTicks)
            {
                throw new OverflowException();
            }
            return new TimeSpan(-t._ticks);
        }

        public static TimeSpan operator -(TimeSpan t1, TimeSpan t2)
        {
            long result = t1._ticks - t2._ticks;
            long t1Sign = t1._ticks >> 63;

            if ((t1Sign != (t2._ticks >> 63)) && (t1Sign != (result >> 63)))
            {
                // Overflow if signs of operands was different and result's sign was opposite.
                // >> 63 gives the sign bit (either 64 1's or 64 0's).
                throw new OverflowException();
            }
            return new TimeSpan(result);
        }

        public static TimeSpan operator +(TimeSpan t) => t;

        public static TimeSpan operator +(TimeSpan t1, TimeSpan t2)
        {
            long result = t1._ticks + t2._ticks;
            long t1Sign = t1._ticks >> 63;

            if ((t1Sign == (t2._ticks >> 63)) && (t1Sign != (result >> 63)))
            {
                // Overflow if signs of operands was identical and result's sign was opposite.
                // >> 63 gives the sign bit (either 64 1's or 64 0's).
                throw new OverflowException();
            }
            return new TimeSpan(result);
        }

        public static TimeSpan operator *(TimeSpan timeSpan, double factor)
        {
            if (double.IsNaN(factor))
            {
                throw new ArgumentException();
            }

            // Rounding to the nearest tick is as close to the result we would have with unlimited
            // precision as possible, and so likely to have the least potential to surprise.
            double ticks = Math.Round(timeSpan.Ticks * factor);
            return IntervalFromDoubleTicks(ticks);
        }

        public static TimeSpan operator *(double factor, TimeSpan timeSpan) => timeSpan * factor;

        public static TimeSpan operator /(TimeSpan timeSpan, double divisor)
        {
            if (double.IsNaN(divisor))
            {
                throw new ArgumentException();
            }

            double ticks = Math.Round(timeSpan.Ticks / divisor);
            return IntervalFromDoubleTicks(ticks);
        }

        public static double operator /(TimeSpan t1, TimeSpan t2) => t1.Ticks / (double)t2.Ticks;

        public static bool operator ==(TimeSpan t1, TimeSpan t2) => t1._ticks == t2._ticks;

        public static bool operator !=(TimeSpan t1, TimeSpan t2) => t1._ticks != t2._ticks;

        public static bool operator <(TimeSpan t1, TimeSpan t2) => t1._ticks < t2._ticks;

        public static bool operator <=(TimeSpan t1, TimeSpan t2) => t1._ticks <= t2._ticks;

        public static bool operator >(TimeSpan t1, TimeSpan t2) => t1._ticks > t2._ticks;

        public static bool operator >=(TimeSpan t1, TimeSpan t2) => t1._ticks >= t2._ticks;
    }

    internal static class Marvin
    {
        /// <summary>
        /// Compute a Marvin hash and collapse it into a 32-bit hash.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeHash32(ReadOnlySpan<byte> data, ulong seed)
            => ComputeHash32(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(data), (uint)data.Length, (uint)seed, (uint)(seed >> 32));

        /// <summary>
        /// Compute a Marvin hash and collapse it into a 32-bit hash.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ComputeHash32(ref byte data, uint count, uint p0, uint p1)
        {
            // Control flow of this method generally flows top-to-bottom, trying to
            // minimize the number of branches taken for large (>= 8 bytes, 4 chars) inputs.
            // If small inputs (< 8 bytes, 4 chars) are given, this jumps to a "small inputs"
            // handler at the end of the method.

            if (count < 8)
            {
                // We can't run the main loop, but we might still have 4 or more bytes available to us.
                // If so, jump to the 4 .. 7 bytes logic immediately after the main loop.

                if (count >= 4)
                {
                    goto Between4And7BytesRemain;
                }
                else
                {
                    goto InputTooSmallToEnterMainLoop;
                }
            }

            // Main loop - read 8 bytes at a time.
            // The block function is unrolled 2x in this loop.

            uint loopCount = count / 8;

            do
            {
                p0 += Unsafe.ReadUnaligned<uint>(ref data);
                uint nextUInt32 = Unsafe.ReadUnaligned<uint>(ref Unsafe.AddByteOffset(ref data, 4));

                // One block round for each of the 32-bit integers we just read, 2x rounds total.

                Block(ref p0, ref p1);
                p0 += nextUInt32;
                Block(ref p0, ref p1);

                // Bump the data reference pointer and decrement the loop count.

                data = ref Unsafe.AddByteOffset(ref data, 8);
            } while (--loopCount > 0);

            // n.b. We've not been updating the original 'count' parameter, so its actual value is
            // still the original data length. However, we can still rely on its least significant
            // 3 bits to tell us how much data remains (0 .. 7 bytes) after the loop above is
            // completed.

            if ((count & 0b_0100) == 0)
            {
                goto DoFinalPartialRead;
            }

        Between4And7BytesRemain:

            // If after finishing the main loop we still have 4 or more leftover bytes, or if we had
            // 4 .. 7 bytes to begin with and couldn't enter the loop in the first place, we need to
            // consume 4 bytes immediately and send them through one round of the block function.

            p0 += Unsafe.ReadUnaligned<uint>(ref data);
            Block(ref p0, ref p1);

        DoFinalPartialRead:

            // Finally, we have 0 .. 3 bytes leftover. Since we know the original data length was at
            // least 4 bytes (smaller lengths are handled at the end of this routine), we can safely
            // read the 4 bytes at the end of the buffer without reading past the beginning of the
            // original buffer. This necessarily means the data we're about to read will overlap with
            // some data we've already processed, but we can handle that below.

            uint partialResult = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref Unsafe.AddByteOffset(ref data, (nuint)count & 7), -4));

            // The 'partialResult' local above contains any data we have yet to read, plus some number
            // of bytes which we've already read from the buffer. An example of this is given below
            // for little-endian architectures. In this table, AA BB CC are the bytes which we still
            // need to consume, and ## are bytes which we want to throw away since we've already
            // consumed them as part of a previous read.
            //
            //                                                    (partialResult contains)   (we want it to contain)
            // count mod 4 = 0 -> [ ## ## ## ## |             ] -> 0x####_####             -> 0x0000_0080
            // count mod 4 = 1 -> [ ## ## ## ## | AA          ] -> 0xAA##_####             -> 0x0000_80AA
            // count mod 4 = 2 -> [ ## ## ## ## | AA BB       ] -> 0xBBAA_####             -> 0x0080_BBAA
            // count mod 4 = 3 -> [ ## ## ## ## | AA BB CC    ] -> 0xCCBB_AA##             -> 0x80CC_BBAA

            count = ~count << 3;

            if (BitConverter.IsLittleEndian)
            {
                partialResult >>= 8; // make some room for the 0x80 byte
                partialResult |= 0x8000_0000u; // put the 0x80 byte at the beginning
                partialResult >>= (int)count & 0x1F; // shift out all previously consumed bytes
            }
            else
            {
                partialResult <<= 8; // make some room for the 0x80 byte
                partialResult |= 0x80u; // put the 0x80 byte at the end
                partialResult <<= (int)count & 0x1F; // shift out all previously consumed bytes
            }

        DoFinalRoundsAndReturn:

            // Now that we've computed the final partial result, merge it in and run two rounds of
            // the block function to finish out the Marvin algorithm.

            p0 += partialResult;
            Block(ref p0, ref p1);
            Block(ref p0, ref p1);

            return (int)(p1 ^ p0);

        InputTooSmallToEnterMainLoop:

            // We had only 0 .. 3 bytes to begin with, so we can't perform any 32-bit reads.
            // This means that we're going to be building up the final result right away and
            // will only ever run two rounds total of the block function. Let's initialize
            // the partial result to "no data".

            if (BitConverter.IsLittleEndian)
            {
                partialResult = 0x80u;
            }
            else
            {
                partialResult = 0x80000000u;
            }

            if ((count & 0b_0001) != 0)
            {
                // If the buffer is 1 or 3 bytes in length, let's read a single byte now
                // and merge it into our partial result. This will result in partialResult
                // having one of the two values below, where AA BB CC are the buffer bytes.
                //
                //                  (little-endian / big-endian)
                // [ AA          ]  -> 0x0000_80AA / 0xAA80_0000
                // [ AA BB CC    ]  -> 0x0000_80CC / 0xCC80_0000

                partialResult = Unsafe.AddByteOffset(ref data, (nuint)count & 2);

                if (BitConverter.IsLittleEndian)
                {
                    partialResult |= 0x8000;
                }
                else
                {
                    partialResult <<= 24;
                    partialResult |= 0x800000u;
                }
            }

            if ((count & 0b_0010) != 0)
            {
                // If the buffer is 2 or 3 bytes in length, let's read a single ushort now
                // and merge it into the partial result. This will result in partialResult
                // having one of the two values below, where AA BB CC are the buffer bytes.
                //
                //                  (little-endian / big-endian)
                // [ AA BB       ]  -> 0x0080_BBAA / 0xAABB_8000
                // [ AA BB CC    ]  -> 0x80CC_BBAA / 0xAABB_CC80 (carried over from above)

                if (BitConverter.IsLittleEndian)
                {
                    partialResult <<= 16;
                    partialResult |= (uint)Unsafe.ReadUnaligned<ushort>(ref data);
                }
                else
                {
                    partialResult |= (uint)Unsafe.ReadUnaligned<ushort>(ref data);
                    partialResult = System.Numerics.BitOperations.RotateLeft(partialResult, 16);
                }
            }

            // Everything is consumed! Go perform the final rounds and return.

            goto DoFinalRoundsAndReturn;
        }

        /// <summary>
        /// Compute a Marvin OrdinalIgnoreCase hash and collapse it into a 32-bit hash.
        /// n.b. <paramref name="count"/> is specified as char count, not byte count.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ComputeHash32OrdinalIgnoreCase(ref char data, int count, uint p0, uint p1)
        {
            uint ucount = (uint)count; // in chars
            nuint byteOffset = 0; // in bytes
            uint tempValue;

            // We operate on 32-bit integers (two chars) at a time.

            while (ucount >= 2)
            {
                tempValue = Unsafe.ReadUnaligned<uint>(ref Unsafe.As<char, byte>(ref Unsafe.AddByteOffset(ref data, byteOffset)));
                if (!System.Text.Unicode.Utf16Utility.AllCharsInUInt32AreAscii(tempValue))
                {
                    goto NotAscii;
                }
                p0 += System.Text.Unicode.Utf16Utility.ConvertAllAsciiCharsInUInt32ToUppercase(tempValue);
                Block(ref p0, ref p1);

                byteOffset += 4;
                ucount -= 2;
            }

            // We have either one char (16 bits) or zero chars left over.

            if (ucount > 0)
            {
                tempValue = Unsafe.AddByteOffset(ref data, byteOffset);
                if (tempValue > 0x7Fu)
                {
                    goto NotAscii;
                }

                if (BitConverter.IsLittleEndian)
                {
                    // addition is written with -0x80u to allow fall-through to next statement rather than jmp past it
                    p0 += System.Text.Unicode.Utf16Utility.ConvertAllAsciiCharsInUInt32ToUppercase(tempValue) + (0x800000u - 0x80u);
                }
                else
                {
                    // as above, addition is modified to allow fall-through to next statement rather than jmp past it
                    p0 += (System.Text.Unicode.Utf16Utility.ConvertAllAsciiCharsInUInt32ToUppercase(tempValue) << 16) + 0x8000u - 0x80000000u;
                }
            }
            if (BitConverter.IsLittleEndian)
            {
                p0 += 0x80u;
            }
            else
            {
                p0 += 0x80000000u;
            }

            Block(ref p0, ref p1);
            Block(ref p0, ref p1);

            return (int)(p1 ^ p0);

        NotAscii:
            return ComputeHash32OrdinalIgnoreCaseSlow(ref Unsafe.AddByteOffset(ref data, byteOffset), (int)ucount, p0, p1);
        }

        private static unsafe int ComputeHash32OrdinalIgnoreCaseSlow(ref char data, int count, uint p0, uint p1)
        {
            char[]? borrowedArr = null;
            Span<char> scratch = (uint)count <= 64 ? stackalloc char[64] : (borrowedArr = System.Buffers.ArrayPool<char>.Shared.Rent(count));

            int charsWritten = Globalization.Ordinal.ToUpperOrdinal(new ReadOnlySpan<char>(ref data, count), scratch);

            // Slice the array to the size returned by ToUpperInvariant.
            // Multiplication below will not overflow since going from positive Int32 to UInt32.
            int hash = ComputeHash32(ref Unsafe.As<char, byte>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(scratch)), (uint)charsWritten * 2, p0, p1);

            // Return the borrowed array if necessary.
            if (borrowedArr != null)
            {
                System.Buffers.ArrayPool<char>.Shared.Return(borrowedArr);
            }

            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Block(ref uint rp0, ref uint rp1)
        {
            uint p0 = rp0;
            uint p1 = rp1;

            p1 ^= p0;
            p0 = System.Numerics.BitOperations.RotateLeft(p0, 20);

            p0 += p1;
            p1 = System.Numerics.BitOperations.RotateLeft(p1, 9);

            p1 ^= p0;
            p0 = System.Numerics.BitOperations.RotateLeft(p0, 27);

            p0 += p1;
            p1 = System.Numerics.BitOperations.RotateLeft(p1, 19);

            rp0 = p0;
            rp1 = p1;
        }

        public static ulong DefaultSeed { get; } = GenerateSeed();

        private static unsafe ulong GenerateSeed()
        {
            ulong seed = 0;

            return seed;
        }
    }

    public static class Convert
    {
        public static bool ToBoolean(bool value)
        {
            return value;
        }

        public static bool ToBoolean(sbyte value)
        {
            return value != 0;
        }

        public static bool ToBoolean(byte value)
        {
            return value != 0;
        }

        public static bool ToBoolean(short value)
        {
            return value != 0;
        }

        public static bool ToBoolean(ushort value)
        {
            return value != 0;
        }

        public static bool ToBoolean(int value)
        {
            return value != 0;
        }

        public static bool ToBoolean(uint value)
        {
            return value != 0;
        }

        public static bool ToBoolean(long value)
        {
            return value != 0;
        }

        public static bool ToBoolean(ulong value)
        {
            return value != 0;
        }

        public static bool ToBoolean(float value)
        {
            return value != 0;
        }

        public static bool ToBoolean(double value)
        {
            return value != 0;
        }

        public static char ToChar(char value)
        {
            return value;
        }

        public static char ToChar(sbyte value)
        {
            if (value < 0) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(byte value)
        {
            return (char)value;
        }

        public static char ToChar(short value)
        {
            if (value < 0) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(ushort value)
        {
            return (char)value;
        }

        public static char ToChar(int value) => ToChar((uint)value);

        public static char ToChar(uint value)
        {
            if (value > char.MaxValue) ThrowCharOverflowException();
            return (char)value;
        }

        public static char ToChar(long value) => ToChar((ulong)value);

        public static char ToChar(ulong value)
        {
            if (value > char.MaxValue) ThrowCharOverflowException();
            return (char)value;
        }

        public static sbyte ToSByte(bool value)
        {
            return value ? (sbyte)bool.True : (sbyte)bool.False;
        }

        public static sbyte ToSByte(sbyte value)
        {
            return value;
        }

        public static sbyte ToSByte(char value)
        {
            if (value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(byte value)
        {
            if (value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(short value)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(ushort value)
        {
            if (value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(int value)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(uint value)
        {
            if (value > (uint)sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(long value)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(ulong value)
        {
            if (value > (ulong)sbyte.MaxValue) ThrowSByteOverflowException();
            return (sbyte)value;
        }

        public static sbyte ToSByte(float value)
        {
            return ToSByte((double)value);
        }

        public static sbyte ToSByte(double value)
        {
            return ToSByte(ToInt32(value));
        }

        public static byte ToByte(bool value)
        {
            return value ? (byte)bool.True : (byte)bool.False;
        }

        public static byte ToByte(byte value)
        {
            return value;
        }

        public static byte ToByte(char value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(sbyte value)
        {
            if (value < 0) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(short value)
        {
            if ((uint)value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(ushort value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(int value) => ToByte((uint)value);

        public static byte ToByte(uint value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(long value) => ToByte((ulong)value);

        public static byte ToByte(ulong value)
        {
            if (value > byte.MaxValue) ThrowByteOverflowException();
            return (byte)value;
        }

        public static byte ToByte(float value)
        {
            return ToByte((double)value);
        }

        public static byte ToByte(double value)
        {
            return ToByte(ToInt32(value));
        }

        public static short ToInt16(bool value)
        {
            return value ? (short)bool.True : (short)bool.False;
        }

        public static short ToInt16(char value)
        {
            if (value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(sbyte value)
        {
            return value;
        }

        public static short ToInt16(byte value)
        {
            return value;
        }

        public static short ToInt16(ushort value)
        {
            if (value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(int value)
        {
            if (value < short.MinValue || value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(uint value)
        {
            if (value > (uint)short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(short value)
        {
            return value;
        }

        public static short ToInt16(long value)
        {
            if (value < short.MinValue || value > short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(ulong value)
        {
            if (value > (ulong)short.MaxValue) ThrowInt16OverflowException();
            return (short)value;
        }

        public static short ToInt16(float value)
        {
            return ToInt16((double)value);
        }

        public static short ToInt16(double value)
        {
            return ToInt16(ToInt32(value));
        }

        public static ushort ToUInt16(bool value)
        {
            return value ? (ushort)bool.True : (ushort)bool.False;
        }

        public static ushort ToUInt16(char value)
        {
            return value;
        }

        public static ushort ToUInt16(sbyte value)
        {
            if (value < 0) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(byte value)
        {
            return value;
        }

        public static ushort ToUInt16(short value)
        {
            if (value < 0) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(int value) => ToUInt16((uint)value);

        public static ushort ToUInt16(ushort value)
        {
            return value;
        }

        public static ushort ToUInt16(uint value)
        {
            if (value > ushort.MaxValue) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(long value) => ToUInt16((ulong)value);

        public static ushort ToUInt16(ulong value)
        {
            if (value > ushort.MaxValue) ThrowUInt16OverflowException();
            return (ushort)value;
        }

        public static ushort ToUInt16(float value)
        {
            return ToUInt16((double)value);
        }

        public static ushort ToUInt16(double value)
        {
            return ToUInt16(ToInt32(value));
        }

        public static int ToInt32(bool value)
        {
            return value ? bool.True : bool.False;
        }

        public static int ToInt32(char value)
        {
            return value;
        }

        public static int ToInt32(sbyte value)
        {
            return value;
        }

        public static int ToInt32(byte value)
        {
            return value;
        }

        public static int ToInt32(short value)
        {
            return value;
        }

        public static int ToInt32(ushort value)
        {
            return value;
        }

        public static int ToInt32(uint value)
        {
            if ((int)value < 0) ThrowInt32OverflowException();
            return (int)value;
        }

        public static int ToInt32(int value)
        {
            return value;
        }

        public static int ToInt32(long value)
        {
            if (value < int.MinValue || value > int.MaxValue) ThrowInt32OverflowException();
            return (int)value;
        }

        public static int ToInt32(ulong value)
        {
            if (value > int.MaxValue) ThrowInt32OverflowException();
            return (int)value;
        }

        public static int ToInt32(float value)
        {
            return ToInt32((double)value);
        }

        public static int ToInt32(double value)
        {
            if (value >= 0)
            {
                if (value < 2147483647.5)
                {
                    int result = (int)value;
                    double dif = value - result;
                    if (dif > 0.5 || dif == 0.5 && (result & 1) != 0) result++;
                    return result;
                }
            }
            else
            {
                if (value >= -2147483648.5)
                {
                    int result = (int)value;
                    double dif = value - result;
                    if (dif < -0.5 || dif == -0.5 && (result & 1) != 0) result--;
                    return result;
                }
            }
            throw new OverflowException();
        }


        public static uint ToUInt32(bool value)
        {
            return value ? (uint)bool.True : (uint)bool.False;
        }

        public static uint ToUInt32(char value)
        {
            return value;
        }

        public static uint ToUInt32(sbyte value)
        {
            if (value < 0) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(byte value)
        {
            return value;
        }

        public static uint ToUInt32(short value)
        {
            if (value < 0) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(ushort value)
        {
            return value;
        }

        public static uint ToUInt32(int value)
        {
            if (value < 0) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(uint value)
        {
            return value;
        }

        public static uint ToUInt32(long value) => ToUInt32((ulong)value);

        public static uint ToUInt32(ulong value)
        {
            if (value > uint.MaxValue) ThrowUInt32OverflowException();
            return (uint)value;
        }

        public static uint ToUInt32(float value)
        {
            return ToUInt32((double)value);
        }

        public static uint ToUInt32(double value)
        {
            if (value >= -0.5 && value < 4294967295.5)
            {
                uint result = (uint)value;
                double dif = value - result;
                if (dif > 0.5 || dif == 0.5 && (result & 1) != 0) result++;
                return result;
            }
            throw new OverflowException();
        }


        public static long ToInt64(bool value)
        {
            return value ? bool.True : bool.False;
        }

        public static long ToInt64(char value)
        {
            return value;
        }

        public static long ToInt64(sbyte value)
        {
            return value;
        }

        public static long ToInt64(byte value)
        {
            return value;
        }

        public static long ToInt64(short value)
        {
            return value;
        }

        public static long ToInt64(ushort value)
        {
            return value;
        }

        public static long ToInt64(int value)
        {
            return value;
        }

        public static long ToInt64(uint value)
        {
            return value;
        }

        public static long ToInt64(ulong value)
        {
            if ((long)value < 0) ThrowInt64OverflowException();
            return (long)value;
        }

        public static long ToInt64(long value)
        {
            return value;
        }

        public static long ToInt64(float value)
        {
            return ToInt64((double)value);
        }

        public static long ToInt64(double value)
        {
            return checked((long)Math.Round(value));
        }


        public static ulong ToUInt64(bool value)
        {
            return value ? (ulong)bool.True : (ulong)bool.False;
        }

        public static ulong ToUInt64(char value)
        {
            return value;
        }

        public static ulong ToUInt64(sbyte value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(byte value)
        {
            return value;
        }

        public static ulong ToUInt64(short value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(ushort value)
        {
            return value;
        }

        public static ulong ToUInt64(int value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(uint value)
        {
            return value;
        }

        public static ulong ToUInt64(long value)
        {
            if (value < 0) ThrowUInt64OverflowException();
            return (ulong)value;
        }

        public static ulong ToUInt64(ulong value)
        {
            return value;
        }

        public static ulong ToUInt64(float value)
        {
            return ToUInt64((double)value);
        }

        public static ulong ToUInt64(double value)
        {
            return checked((ulong)Math.Round(value));
        }

        private static void ThrowCharOverflowException() { throw new OverflowException(); }

        private static void ThrowByteOverflowException() { throw new OverflowException(); }

        private static void ThrowSByteOverflowException() { throw new OverflowException(); }

        private static void ThrowInt16OverflowException() { throw new OverflowException(); }

        private static void ThrowUInt16OverflowException() { throw new OverflowException(); }

        private static void ThrowInt32OverflowException() { throw new OverflowException(); }

        private static void ThrowUInt32OverflowException() { throw new OverflowException(); }

        private static void ThrowInt64OverflowException() { throw new OverflowException(); }

        private static void ThrowUInt64OverflowException() { throw new OverflowException(); }
    }
    public abstract class StringComparer : IComparer
    {
        public static StringComparer InvariantCulture => CultureAwareComparer.InvariantCaseSensitiveInstance;

        public static StringComparer InvariantCultureIgnoreCase => CultureAwareComparer.InvariantIgnoreCaseInstance;

        public static StringComparer CurrentCulture =>
            new CultureAwareComparer(System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CompareOptions.None);

        public static StringComparer CurrentCultureIgnoreCase =>
            new CultureAwareComparer(System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CompareOptions.IgnoreCase);

        public static StringComparer Ordinal => OrdinalCaseSensitiveComparer.Instance;

        public static StringComparer OrdinalIgnoreCase => OrdinalIgnoreCaseComparer.Instance;

        // Convert a StringComparison to a StringComparer
        public static StringComparer FromComparison(StringComparison comparisonType)
        {
            return comparisonType switch
            {
                StringComparison.CurrentCulture => CurrentCulture,
                StringComparison.CurrentCultureIgnoreCase => CurrentCultureIgnoreCase,
                StringComparison.InvariantCulture => InvariantCulture,
                StringComparison.InvariantCultureIgnoreCase => InvariantCultureIgnoreCase,
                StringComparison.Ordinal => Ordinal,
                StringComparison.OrdinalIgnoreCase => OrdinalIgnoreCase,
                _ => throw new ArgumentException(),
            };
        }

        private protected virtual bool IsWellKnownOrdinalComparerCore(out bool ignoreCase)
        {
            // unless specialized comparer overrides this, we're not a well-known ordinal comparer
            ignoreCase = default;
            return false;
        }
        private protected virtual bool IsWellKnownCultureAwareComparerCore(
            [NotNullWhen(true)] out System.Globalization.CompareInfo? compareInfo, out System.Globalization.CompareOptions compareOptions)
        {
            // unless specialized comparer overrides this, we're not a well-known culture-aware comparer
            compareInfo = default;
            compareOptions = default;
            return false;
        }

        public int GetHashCode(object obj)
        {
            if (obj is null) throw new ArgumentNullException();

            if (obj is string s)
            {
                return GetHashCode(s);
            }
            return obj.GetHashCode();
        }
        public abstract int Compare(string? x, string? y);
        public abstract bool Equals(string? x, string? y);
        public abstract int GetHashCode(string obj);
    }
    public class OrdinalComparer : StringComparer, IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
    {
        private readonly bool _ignoreCase; // Do not rename

        internal OrdinalComparer(bool ignoreCase)
        {
            _ignoreCase = ignoreCase;
        }

        // Equals method for the comparer itself.
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is not OrdinalComparer comparer)
            {
                return false;
            }
            return this._ignoreCase == comparer._ignoreCase;
        }

        public override int GetHashCode()
        {
            int hashCode = nameof(OrdinalComparer).GetHashCode();
            return _ignoreCase ? (~hashCode) : hashCode;
        }

        public override int GetHashCode(string obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException();
            }

            if (_ignoreCase)
            {
                return obj.GetHashCodeOrdinalIgnoreCase();
            }

            return obj.GetHashCode();
        }
    }
    internal sealed class OrdinalCaseSensitiveComparer : OrdinalComparer, IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
    {
        internal static readonly OrdinalCaseSensitiveComparer Instance = new OrdinalCaseSensitiveComparer();

        private OrdinalCaseSensitiveComparer() : base(false)
        {
        }

        public override int Compare(string? x, string? y) => string.CompareOrdinal(x, y);

        public override bool Equals(string? x, string? y) => string.Equals(x, y);

        public override int GetHashCode(string obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }
            return obj.GetHashCode();
        }
    }
    internal sealed class OrdinalIgnoreCaseComparer : OrdinalComparer, IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
    {
        internal static readonly OrdinalIgnoreCaseComparer Instance = new OrdinalIgnoreCaseComparer();

        private OrdinalIgnoreCaseComparer() : base(true)
        {
        }

        public override int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            return Globalization.Ordinal.CompareStringIgnoreCase(ref x.GetRawStringData(), x.Length, ref y.GetRawStringData(), y.Length);
        }

        public override bool Equals(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            if (x.Length != y.Length)
            {
                return false;
            }

            return Globalization.Ordinal.EqualsIgnoreCase(ref x.GetRawStringData(), ref y.GetRawStringData(), x.Length);
        }

        public override int GetHashCode(string obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException();
            }
            return obj.GetHashCodeOrdinalIgnoreCase();
        }
    }
    public sealed class CultureAwareComparer : StringComparer
    {
        internal static readonly CultureAwareComparer InvariantCaseSensitiveInstance =
            new CultureAwareComparer(System.Globalization.CompareInfo.Invariant, System.Globalization.CompareOptions.None);
        internal static readonly CultureAwareComparer InvariantIgnoreCaseInstance =
            new CultureAwareComparer(System.Globalization.CompareInfo.Invariant, System.Globalization.CompareOptions.IgnoreCase);

        private const System.Globalization.CompareOptions ValidCompareMaskOffFlags =
            ~(System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreSymbols |
            System.Globalization.CompareOptions.IgnoreNonSpace | System.Globalization.CompareOptions.IgnoreKanaType |
              System.Globalization.CompareOptions.IgnoreWidth | System.Globalization.CompareOptions.NumericOrdering | System.Globalization.CompareOptions.StringSort);

        private readonly System.Globalization.CompareInfo _compareInfo; // Do not rename
        private readonly System.Globalization.CompareOptions _options;

        internal CultureAwareComparer(System.Globalization.CultureInfo culture, System.Globalization.CompareOptions options) : this(culture.CompareInfo, options) { }

        internal CultureAwareComparer(System.Globalization.CompareInfo compareInfo, System.Globalization.CompareOptions options)
        {
            _compareInfo = compareInfo;

            if ((options & ValidCompareMaskOffFlags) != 0)
            {
                throw new ArgumentException();
            }
            _options = options;
        }
    }
    // interfaces
    public interface IBitwiseOperators<TSelf, TOther, TResult>
        where TSelf : IBitwiseOperators<TSelf, TOther, TResult>?
    {

    }
    public interface INumber<TSelf>
        : System.Numerics.INumberBase<TSelf>
        where TSelf : INumber<TSelf>?
    {

    }
    public interface IBinaryNumber<TSelf>
        : IBitwiseOperators<TSelf, TSelf, TSelf>,
          INumber<TSelf>
        where TSelf : IBinaryNumber<TSelf>?
    {

    }
    public interface IShiftOperators<TSelf, TOther, TResult>
        where TSelf : IShiftOperators<TSelf, TOther, TResult>?
    {

    }
    public interface IBinaryInteger<TSelf>
        : IBinaryNumber<TSelf>,
          IShiftOperators<TSelf, int, TSelf>
        where TSelf : IBinaryInteger<TSelf>?
    {

    }
    internal interface IUtfChar<TSelf> :
        IBinaryInteger<TSelf>
        where TSelf : unmanaged, IUtfChar<TSelf>
    {
        /// <summary>Casts the specified value to this type.</summary>
        public static abstract TSelf CastFrom(byte value);

        /// <summary>Casts the specified value to this type.</summary>
        public static abstract TSelf CastFrom(char value);

        /// <summary>Casts the specified value to this type.</summary>
        public static abstract TSelf CastFrom(int value);

        /// <summary>Casts the specified value to this type.</summary>
        public static abstract TSelf CastFrom(uint value);

        /// <summary>Casts the specified value to this type.</summary>
        public static abstract TSelf CastFrom(ulong value);
    }
    public interface ITuple
    {
        int Length { get; }

        object this[int index] { get; }
    }
    public interface IDisposable
    {
        void Dispose();
    }
    public interface IComparable
    {
        int CompareTo(object? obj);
    }
    public interface IComparable<in T> where T : allows ref struct
    {
        int CompareTo(T? other);
    }
    public interface IConvertible
    {
        TypeCode GetTypeCode();

        bool ToBoolean(IFormatProvider? provider);
        char ToChar(IFormatProvider? provider);
        sbyte ToSByte(IFormatProvider? provider);
        byte ToByte(IFormatProvider? provider);
        short ToInt16(IFormatProvider? provider);
        ushort ToUInt16(IFormatProvider? provider);
        int ToInt32(IFormatProvider? provider);
        uint ToUInt32(IFormatProvider? provider);
        long ToInt64(IFormatProvider? provider);
        ulong ToUInt64(IFormatProvider? provider);
        float ToSingle(IFormatProvider? provider);
        double ToDouble(IFormatProvider? provider);
        decimal ToDecimal(IFormatProvider? provider);
        DateTime ToDateTime(IFormatProvider? provider);
        string ToString(IFormatProvider? provider);
        object ToType(Type conversionType, IFormatProvider? provider);
    }
    public interface IFormattable
    {
        string ToString(string? format, IFormatProvider? formatProvider);
    }
    /// <summary>
    /// Provides a mechanism for retrieving an object to control formatting.
    /// </summary>
    public interface IFormatProvider
    {
        object? GetFormat(Type? formatType);
    }
    /// <summary>
    /// Defines a method that supports custom formatting of the value of an object.
    /// </summary>
    public interface ICustomFormatter
    {
        string Format(string? format, object? arg, IFormatProvider? formatProvider);
    }
    public interface IEquatable<T> where T : allows ref struct // invariant due to questionable semantics around equality and inheritance
    {
        /// <summary>Indicates whether the current object is equal to another object of the same type.</summary>
        bool Equals(T? other);
    }
    // math
    public enum MidpointRounding
    {
        ToEven = 0,
        AwayFromZero = 1,
        ToZero = 2,
        ToNegativeInfinity = 3,
        ToPositiveInfinity = 4
    }
    public static class BitConverter
    {
        public static readonly bool IsLittleEndian = true;

        public static byte[] GetBytes(bool value) => new byte[] { (value ? (byte)1 : (byte)0) };

        public static byte[] GetBytes(char value)
        {
            byte[] bytes = new byte[sizeof(char)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, char value)
        {
            if (destination.Length < sizeof(char))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(short value)
        {
            byte[] bytes = new byte[sizeof(short)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, short value)
        {
            if (destination.Length < sizeof(short))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(int value)
        {
            byte[] bytes = new byte[sizeof(int)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, int value)
        {
            if (destination.Length < sizeof(int))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(long value)
        {
            byte[] bytes = new byte[sizeof(long)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, long value)
        {
            if (destination.Length < sizeof(long))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(ushort value)
        {
            byte[] bytes = new byte[sizeof(ushort)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, ushort value)
        {
            if (destination.Length < sizeof(ushort))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(uint value)
        {
            byte[] bytes = new byte[sizeof(uint)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, uint value)
        {
            if (destination.Length < sizeof(uint))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(ulong value)
        {
            byte[] bytes = new byte[sizeof(ulong)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, ulong value)
        {
            if (destination.Length < sizeof(ulong))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(float value)
        {
            byte[] bytes = new byte[sizeof(float)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, float value)
        {
            if (destination.Length < sizeof(float))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static byte[] GetBytes(double value)
        {
            byte[] bytes = new byte[sizeof(double)];
            bool success = TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, double value)
        {
            if (destination.Length < sizeof(double))
                return false;

            Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), value);
            return true;
        }

        public static char ToChar(byte[] value, int startIndex) => unchecked((char)ToInt16(value, startIndex));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static char ToChar(ReadOnlySpan<byte> value)
        {
            if (value.Length < sizeof(char))
                throw new ArgumentOutOfRangeException();
            return Unsafe.ReadUnaligned<char>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value));
        }

        public static short ToInt16(byte[] value, int startIndex)
        {
            if (value == null)
                throw new ArgumentNullException();
            if (unchecked((uint)startIndex) >= unchecked((uint)value.Length))
                throw new ArgumentOutOfRangeException();
            if (startIndex > value.Length - sizeof(short))
                throw new ArgumentException();

            return Unsafe.ReadUnaligned<short>(ref value[startIndex]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short ToInt16(ReadOnlySpan<byte> value)
        {
            if (value.Length < sizeof(short))
                throw new ArgumentOutOfRangeException();
            return Unsafe.ReadUnaligned<short>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value));
        }

        public static int ToInt32(byte[] value, int startIndex)
        {
            if (value == null)
                throw new ArgumentNullException();
            if (unchecked((uint)startIndex) >= unchecked((uint)value.Length))
                throw new ArgumentOutOfRangeException();
            if (startIndex > value.Length - sizeof(int))
                throw new ArgumentException();

            return Unsafe.ReadUnaligned<int>(ref value[startIndex]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToInt32(ReadOnlySpan<byte> value)
        {
            if (value.Length < sizeof(int))
                throw new ArgumentOutOfRangeException();
            return Unsafe.ReadUnaligned<int>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value));
        }

        public static long ToInt64(byte[] value, int startIndex)
        {
            if (value == null)
                throw new ArgumentNullException();
            if (unchecked((uint)startIndex) >= unchecked((uint)value.Length))
                throw new ArgumentOutOfRangeException();
            if (startIndex > value.Length - sizeof(long))
                throw new ArgumentException();

            return Unsafe.ReadUnaligned<long>(ref value[startIndex]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ToInt64(ReadOnlySpan<byte> value)
        {
            if (value.Length < sizeof(long))
                throw new ArgumentOutOfRangeException();
            return Unsafe.ReadUnaligned<long>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(value));
        }

        public static long DoubleToInt64Bits(double value) => Unsafe.BitCast<double, long>(value);
        public static double Int64BitsToDouble(long value) => Unsafe.BitCast<long, double>(value);


        public static int SingleToInt32Bits(float value) => Unsafe.BitCast<float, int>(value);
        public static float Int32BitsToSingle(int value) => Unsafe.BitCast<int, float>(value);

        public static ulong DoubleToUInt64Bits(double value) => Unsafe.BitCast<double, ulong>(value);
        public static double UInt64BitsToDouble(ulong value) => Unsafe.BitCast<ulong, double>(value);

        public static uint SingleToUInt32Bits(float value) => Unsafe.BitCast<float, uint>(value);
        public static float UInt32BitsToSingle(uint value) => Unsafe.BitCast<uint, float>(value);
    }
    public static class MathF
    {
        public const float E = 2.71828183f;

        public const float PI = 3.14159265f;

        public const float Tau = 6.283185307f;

        private const int maxRoundingDigits = 6;

        private const float singleRoundLimit = 1e8f;

        private const float SCALEB_C1 = 1.7014118E+38f; // 0x1p127f

        private const float SCALEB_C2 = 1.1754944E-38f; // 0x1p-126f

        private const float SCALEB_C3 = 16777216f; // 0x1p24f

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Abs(float x)
        {
            return Math.Abs(x);
        }

        public static float Sqrt(float x)
        {
            const uint SignMask = 0x8000_0000u;
            const uint ExponentMask = 0x7F80_0000u;
            const uint SignificandMask = 0x007F_FFFFu;
            const uint HiddenBit = 0x0080_0000u;
            const uint PositiveInfinityBits = 0x7F80_0000u;

            uint ix = BitConverter.SingleToUInt32Bits(x);

            // NaN / infinities
            if ((ix & ExponentMask) == ExponentMask)
            {
                // NaN
                if ((ix & SignificandMask) != 0)
                    return x + x;

                // +Infinity to +Infinity, -Infinity to NaN
                return (ix & SignMask) == 0 ? x : float.NaN;
            }

            // Negative values
            if ((ix & SignMask) != 0)
            {
                // sqrt(-0.0f) == -0.0f
                if ((ix & ~SignMask) == 0)
                    return x;

                return float.NaN;
            }

            // +0.0f
            if (ix == 0)
                return x;

            int m = (int)(ix >> 23);

            // Normalize subnormal input
            if (m == 0)
            {
                int i = 0;

                while ((ix & HiddenBit) == 0)
                {
                    i++;
                    ix <<= 1;
                }

                m -= i - 1;
            }

            m -= 127;
            ix = (ix & SignificandMask) | HiddenBit;

            if ((m & 1) != 0)
                ix += ix;

            m >>= 1;

            ix += ix;

            uint q = 0;
            uint s = 0;
            uint r = 0x0100_0000u;

            while (r != 0)
            {
                uint t = s + r;

                if (t <= ix)
                {
                    s = t + r;
                    ix -= t;
                    q += r;
                }

                ix += ix;
                r >>= 1;
            }

            // Round to nearest even
            if (ix != 0)
                q += q & 1u;

            ix = (q >> 1) + 0x3F00_0000u;
            ix += unchecked((uint)(m << 23));

            return BitConverter.UInt32BitsToSingle(ix);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe (float Sin, float Cos) SinCos(float x)
        {
            float sin, cos;
            SinCos(x, &sin, &cos);
            return (sin, cos);
        }
        public static unsafe float Sin(float x)
        {
            float sin, cos;
            SinCos(x, &sin, &cos);
            return sin;
        }

        public static unsafe float Cos(float x)
        {
            float sin, cos;
            SinCos(x, &sin, &cos);
            return cos;
        }

        private static unsafe void SinCos(float x, float* sin, float* cos)
        {
            const uint ExponentMask = 0x7F80_0000u;

            uint ux = BitConverter.SingleToUInt32Bits(x);
            // NaN and infinities
            if ((ux & ExponentMask) == ExponentMask)
            {
                *sin = float.NaN;
                *cos = float.NaN;
                return;
            }
            // zero
            if ((ux & 0x7FFF_FFFFu) == 0)
            {
                *sin = x;
                *cos = 1.0f;
                return;
            }

            const double PiOver2 = 1.57079632679489661923132169163975144;
            const double TwoPi = 6.28318530717958647692528676655900576;
            const double TwoOverPi = 0.636619772367581343075535053490057448;

            double y = (double)x % TwoPi;
            int q = (int)(y * TwoOverPi + (y >= 0.0 ? 0.5 : -0.5));
            double r = y - q * PiOver2;
            double z = r * r;
            double sr = r + r * z *
            (
                -1.66666666666666657415e-1 + z *
                (
                    8.33333333333333321769e-3 + z *
                    (
                        -1.98412698412698412535e-4 + z *
                        (
                            2.75573192239858906526e-6 + z * -2.50521083854417187750e-8
                        )
                    )
                )
            );

            double cr = 1.0 + z *
            (
                -5.00000000000000000000e-1 + z *
                (
                    4.16666666666666643537e-2 + z *
                    (
                        -1.38888888888888894189e-3 + z *
                        (
                            2.48015873015873015688e-5 + z * -2.75573192239858925110e-7
                        )
                    )
                )
            );

            switch (q & 3)
            {
                case 0:
                    *sin = (float)sr;
                    *cos = (float)cr;
                    break;

                case 1:
                    *sin = (float)cr;
                    *cos = (float)-sr;
                    break;

                case 2:
                    *sin = (float)-sr;
                    *cos = (float)-cr;
                    break;

                default:
                    *sin = (float)-cr;
                    *cos = (float)sr;
                    break;
            }
        }
    }
    public static class Math
    {
        public const double E = 2.7182818284590452354;
        public const double PI = 3.14159265358979323846;
        public const double Tau = 6.283185307179586476925;

        private const int maxRoundingDigits = 15;
        private const double doubleRoundLimit = 1e16d;

        private static ReadOnlySpan<double> RoundPower10Double => new double[]
        {
            1E0, 1E1, 1E2, 1E3, 1E4, 1E5, 1E6, 1E7, 1E8,
            1E9, 1E10, 1E11, 1E12, 1E13, 1E14, 1E15
        };

        private const double SCALEB_C1 = 8.98846567431158E+307; // 0x1p1023

        private const double SCALEB_C2 = 2.2250738585072014E-308; // 0x1p-1022

        private const double SCALEB_C3 = 9007199254740992; // 0x1p53

        private const double Ln2 = 0.693147180559945309417232121458176568;
        private const double Ln10 = 2.302585092994045684017991454684364208;
        private const double Log2E = 1.442695040888963407359924681001892137; // 1/ln2

        internal static void ThrowNegateTwosCompOverflow()
        {
            throw new OverflowException();
        }

        public static byte Min(byte val1, byte val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static short Min(short val1, short val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static int Min(int val1, int val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static long Min(long val1, long val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static nint Min(nint val1, nint val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static sbyte Min(sbyte val1, sbyte val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static ushort Min(ushort val1, ushort val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static uint Min(uint val1, uint val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static ulong Min(ulong val1, ulong val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static nuint Min(nuint val1, nuint val2)
        {
            return (val1 <= val2) ? val1 : val2;
        }

        public static byte Max(byte val1, byte val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }
        public static short Max(short val1, short val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static int Max(int val1, int val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static long Max(long val1, long val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static nint Max(nint val1, nint val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static sbyte Max(sbyte val1, sbyte val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static ushort Max(ushort val1, ushort val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static uint Max(uint val1, uint val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static ulong Max(ulong val1, ulong val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static nuint Max(nuint val1, nuint val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Clamp(byte value, byte min, byte max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp(double value, double min, double max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Clamp(short value, short min, short max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int value, int min, int max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Clamp(long value, long min, long max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint Clamp(nint value, nint min, nint max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte Clamp(sbyte value, sbyte min, sbyte max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float value, float min, float max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort Clamp(ushort value, ushort min, ushort max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Clamp(uint value, uint min, uint max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Clamp(ulong value, ulong min, ulong max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint Clamp(nuint value, nuint min, nuint max)
        {
            if (min > max)
            {
                ThrowMinMaxException(min, max);
            }

            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        internal static void ThrowMinMaxException<T>(T min, T max)
        {
            throw new ArgumentException("minimum malue must be lower then maximum value.");
        }

        public static double BitDecrement(double x)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(x);

            if (!double.IsFinite(x))
            {
                // NaN returns NaN
                // -Infinity returns -Infinity
                // +Infinity returns MaxValue
                return (bits == double.PositiveInfinityBits) ? double.MaxValue : x;
            }

            if (bits == double.PositiveZeroBits)
            {
                // +0.0 returns -double.Epsilon
                return -double.Epsilon;
            }

            // Negative values need to be incremented
            // Positive values need to be decremented

            if (double.IsNegative(x))
            {
                bits += 1;
            }
            else
            {
                bits -= 1;
            }
            return BitConverter.UInt64BitsToDouble(bits);
        }

        public static double BitIncrement(double x)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(x);

            if (!double.IsFinite(x))
            {
                // NaN returns NaN
                // -Infinity returns MinValue
                // +Infinity returns +Infinity
                return (bits == double.NegativeInfinityBits) ? double.MinValue : x;
            }

            if (bits == double.NegativeZeroBits)
            {
                // -0.0 returns Epsilon
                return double.Epsilon;
            }

            // Negative values need to be decremented
            // Positive values need to be incremented

            if (double.IsNegative(x))
            {
                bits -= 1;
            }
            else
            {
                bits += 1;
            }
            return BitConverter.UInt64BitsToDouble(bits);
        }

        public static double CopySign(double x, double y)
        {
            // This method is required to work for all inputs,
            // including NaN, so we operate on the raw bits.
            ulong xbits = BitConverter.DoubleToUInt64Bits(x);
            ulong ybits = BitConverter.DoubleToUInt64Bits(y);

            // Remove the sign from x, and remove everything but the sign from y
            // Then, simply OR them to get the correct sign
            return BitConverter.UInt64BitsToDouble((xbits & ~double.SignMask) | (ybits & double.SignMask));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (sbyte Quotient, sbyte Remainder) DivRem(sbyte left, sbyte right)
        {
            sbyte quotient = (sbyte)(left / right);
            return (quotient, (sbyte)(left - (quotient * right)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (byte Quotient, byte Remainder) DivRem(byte left, byte right)
        {
            byte quotient = (byte)(left / right);
            return (quotient, (byte)(left - (quotient * right)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (short Quotient, short Remainder) DivRem(short left, short right)
        {
            short quotient = (short)(left / right);
            return (quotient, (short)(left - (quotient * right)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (ushort Quotient, ushort Remainder) DivRem(ushort left, ushort right)
        {
            ushort quotient = (ushort)(left / right);
            return (quotient, (ushort)(left - (quotient * right)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (int Quotient, int Remainder) DivRem(int left, int right)
        {
            int quotient = left / right;
            return (quotient, left - (quotient * right));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (uint Quotient, uint Remainder) DivRem(uint left, uint right)
        {
            uint quotient = left / right;
            return (quotient, left - (quotient * right));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (long Quotient, long Remainder) DivRem(long left, long right)
        {
            long quotient = left / right;
            return (quotient, left - (quotient * right));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (ulong Quotient, ulong Remainder) DivRem(ulong left, ulong right)
        {
            ulong quotient = left / right;
            return (quotient, left - (quotient * right));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong BigMul(uint a, uint b)
        {
            return ((ulong)a) * b;
        }

        public static long BigMul(int a, int b)
        {
            return ((long)a) * b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong BigMul(ulong a, uint b, out ulong low)
        {
#if TARGET_64BIT
            return Math.BigMul((ulong)a, (ulong)b, out low);
#else
            ulong prodL = ((ulong)(uint)a) * b;
            ulong prodH = (prodL >> 32) + (((ulong)(uint)(a >> 32)) * b);

            low = ((prodH << 32) | (uint)prodL);
            return (prodH >> 32);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong BigMul(uint a, ulong b, out ulong low)
            => BigMul(b, a, out low);

        public static unsafe ulong BigMul(ulong a, ulong b, out ulong low)
        {
            uint al = (uint)a;
            uint ah = (uint)(a >> 32);
            uint bl = (uint)b;
            uint bh = (uint)(b >> 32);

            ulong mull = ((ulong)al) * bl;
            ulong t = ((ulong)ah) * bl + (mull >> 32);
            ulong tl = ((ulong)al) * bh + (uint)t;

            low = tl << 32 | (uint)mull;

            return ((ulong)ah) * bh + (t >> 32) + (tl >> 32);
        }

        public static long BigMul(long a, long b, out long low)
        {
            ulong high = BigMul((ulong)a, (ulong)b, out ulong ulow);
            low = (long)ulow;
            return (long)high - ((a >> 63) & b) - ((b >> 63) & a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt128 BigMul(ulong a, ulong b)
        {
            ulong high = BigMul(a, b, out ulong low);
            return new UInt128(high, low);
        }

        public static Int128 BigMul(long a, long b)
        {
            long high = BigMul(a, b, out long low);
            return new Int128((ulong)high, (ulong)low);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Abs(short value)
        {
            if (value < 0)
            {
                value = (short)-value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Abs(int value)
        {
            if (value < 0)
            {
                value = -value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Abs(long value)
        {
            if (value < 0)
            {
                value = -value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint Abs(nint value)
        {
            if (value < 0)
            {
                value = -value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte Abs(sbyte value)
        {
            if (value < 0)
            {
                value = (sbyte)-value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Abs(double value)
        {
            const ulong mask = 0x7FFFFFFFFFFFFFFF;
            ulong raw = BitConverter.DoubleToUInt64Bits(value);

            return BitConverter.UInt64BitsToDouble(raw & mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Abs(float value)
        {
            const uint mask = 0x7FFFFFFF;
            uint raw = BitConverter.SingleToUInt32Bits(value);

            return BitConverter.UInt32BitsToSingle(raw & mask);
        }

        public static double Truncate(double value)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(value);

            int biasedExp = (int)((bits >> 52) & 0x7FF);

            if (biasedExp == 0x7FF)
                return value;

            int exp = biasedExp - 1023;

            if (exp < 0)
                return BitConverter.UInt64BitsToDouble(bits & 0x8000_0000_0000_0000UL);

            if (exp >= 52)
                return value;

            int fracBits = 52 - exp;
            ulong mask = (1UL << fracBits) - 1UL;
            bits &= ~mask;

            return BitConverter.UInt64BitsToDouble(bits);
        }

        public static float Truncate(float value)
        {
            uint bits = BitConverter.SingleToUInt32Bits(value);

            int biasedExp = (int)((bits >> 23) & 0xFF);

            if (biasedExp == 0xFF)
                return value;

            int exp = biasedExp - 127;

            if (exp < 0)
                return BitConverter.UInt32BitsToSingle(bits & 0x8000_0000u);

            if (exp >= 23)
                return value;

            int fracBits = 23 - exp;
            uint mask = (1u << fracBits) - 1u;
            bits &= ~mask;

            return BitConverter.UInt32BitsToSingle(bits);
        }

        public static double Round(double a)
        {
            const double IntegerBoundary = 4503599627370496.0; // 2^52
            if (Abs(a) >= IntegerBoundary)
            {
                // Values above this boundary don't have a fractional
                // portion and so we can simply return them as-is.
                return a;
            }

            double temp = CopySign(IntegerBoundary, a);
            return CopySign((a + temp) - temp, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe (double Sin, double Cos) SinCos(double x)
        {
            double sin, cos;
            SinCos(x, &sin, &cos);
            return (sin, cos);
        }
        public static unsafe double Sin(double x)
        {
            double sin, cos;
            SinCos(x, &sin, &cos);
            return sin;
        }

        public static unsafe double Cos(double x)
        {
            double sin, cos;
            SinCos(x, &sin, &cos);
            return cos;
        }

        private static unsafe void SinCos(double x, double* sin, double* cos)
        {
            const ulong ExponentMask = 0x7FF0_0000_0000_0000UL;
            const ulong AbsMask = 0x7FFF_FFFF_FFFF_FFFFUL;

            ulong ux = BitConverter.DoubleToUInt64Bits(x);
            // NaN and infinities
            if ((ux & ExponentMask) == ExponentMask)
            {
                *sin = double.NaN;
                *cos = double.NaN;
                return;
            }
            // zero
            if ((ux & AbsMask) == 0)
            {
                *sin = x;
                *cos = 1.0;
                return;
            }

            const double PiOver2 = 1.57079632679489661923132169163975144;
            const double TwoPi = 6.28318530717958647692528676655900576;
            const double TwoOverPi = 0.636619772367581343075535053490057448;

            double y = x % TwoPi;
            int q = (int)(y * TwoOverPi + (y >= 0.0 ? 0.5 : -0.5));
            double r = y - (double)q * PiOver2;
            double z = r * r;

            double sr = r + r * z *
            (
                -1.66666666666666666666e-1 + z *
                (
                    8.33333333333333333322e-3 + z *
                    (
                        -1.98412698412698412550e-4 + z *
                        (
                            2.75573192239858906526e-6 + z *
                            (
                                -2.50521083854417187750e-8 + z *
                                (
                                    1.60590438368216145994e-10 + z *
                                    (
                                        -7.64716373181981647590e-13 + z * 2.81145725434552076320e-15
                                    )
                                )
                            )
                        )
                    )
                )
            );

            double cr = 1.0 + z *
            (
                -5.00000000000000000000e-1 + z *
                (
                    4.16666666666666666667e-2 + z *
                    (
                        -1.38888888888888888889e-3 + z *
                        (
                            2.48015873015873015873e-5 + z *
                            (
                                -2.75573192239858906526e-7 + z *
                                (
                                    2.08767569878680989792e-9 + z *
                                    (
                                        -1.14707455977297247139e-11 + z * 4.77947733238738525335e-14
                                    )
                                )
                            )
                        )
                    )
                )
            );

            switch (q & 3)
            {
                case 0:
                    *sin = sr;
                    *cos = cr;
                    break;

                case 1:
                    *sin = cr;
                    *cos = -sr;
                    break;

                case 2:
                    *sin = -sr;
                    *cos = -cr;
                    break;

                default:
                    *sin = -cr;
                    *cos = sr;
                    break;
            }
        }

        public static double Sqrt(double d)
        {
            const uint Sign32 = 0x8000_0000u;

            ulong bits = BitConverter.DoubleToUInt64Bits(d);
            uint hx = (uint)(bits >> 32);
            uint lx = (uint)bits;

            // NaN / infinities
            if ((hx & 0x7FF0_0000u) == 0x7FF0_0000u)
            {
                // NaN
                if ((hx & 0x000F_FFFFu) != 0 || lx != 0)
                    return d + d;

                // +Infinity to +Infinity, -Infinity to NaN.
                return (hx & Sign32) == 0 ? d : double.NaN;
            }

            // Negative values
            if ((hx & Sign32) != 0)
            {
                // sqrt(-0.0) == -0.0
                if (((hx & 0x7FFF_FFFFu) | lx) == 0)
                    return d;

                return double.NaN;
            }

            // +0.0
            if (((hx & 0x7FFF_FFFFu) | lx) == 0)
                return d;

            int m = (int)(hx >> 20);
            // Normalize subnormal input
            if (m == 0)
            {
                while (hx == 0)
                {
                    m -= 21;
                    hx |= lx >> 11;
                    lx <<= 21;
                }

                int i = 0;
                while ((hx & 0x0010_0000u) == 0)
                {
                    i++;
                    hx <<= 1;
                }

                m -= i - 1;
                hx |= lx >> (32 - i);
                lx <<= i;
            }

            // Unbias exponent and restore the hidden significand bit
            m -= 1023;
            hx = (hx & 0x000F_FFFFu) | 0x0010_0000u;

            // Make exponent even
            if ((m & 1) != 0)
            {
                hx = hx + hx + (lx >> 31);
                lx += lx;
            }

            m >>= 1;

            hx = hx + hx + (lx >> 31);
            lx += lx;

            uint q = 0;
            uint q1 = 0;
            uint s0 = 0;
            uint s1 = 0;

            uint r = 0x0020_0000u;

            while (r != 0)
            {
                uint t = s0 + r;

                if (t <= hx)
                {
                    s0 = t + r;
                    hx -= t;
                    q += r;
                }

                hx = hx + hx + (lx >> 31);
                lx += lx;
                r >>= 1;
            }

            r = Sign32;
            while (r != 0)
            {
                uint t1 = s1 + r;
                uint t = s0;

                if ((t < hx) || ((t == hx) && (t1 <= lx)))
                {
                    s1 = t1 + r;

                    if (((t1 & Sign32) != 0) && ((s1 & Sign32) == 0))
                        s0++;

                    hx -= t;

                    if (lx < t1)
                        hx--;

                    lx -= t1;
                    q1 += r;
                }

                hx = hx + hx + (lx >> 31);
                lx += lx;
                r >>= 1;
            }

            // Round to nearest even
            if ((hx | lx) != 0)
            {
                const double One = 1.0;
                const double Tiny = 1.0e-300;

                double z = One - Tiny;

                if (z >= One)
                {
                    z = One + Tiny;

                    if (q1 == 0xFFFF_FFFFu)
                    {
                        q1 = 0;
                        q++;
                    }
                    else if (z > One)
                    {
                        if (q1 == 0xFFFF_FFFEu)
                            q++;

                        q1 += 2;
                    }
                    else
                    {
                        q1 += q1 & 1u;
                    }
                }
            }

            hx = (q >> 1) + 0x3FE0_0000u;
            lx = q1 >> 1;

            if ((q & 1) != 0)
                lx |= Sign32;

            hx += (uint)(m << 20);

            return BitConverter.UInt64BitsToDouble(((ulong)hx << 32) | lx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Pow(double x, double y)
        {
            if (y == 0.0)
                return 1.0;
            if (x == 1.0)
                return 1.0;
            if (Double.IsNaN(x) || Double.IsNaN(y))
                return Double.NaN;
            if (Double.IsInfinity(y))
            {
                double ax = Abs(x);

                if (ax == 1.0)
                    return 1.0;

                if (y > 0.0)
                    return (ax > 1.0) ? Double.PositiveInfinity : 0.0;
                else
                    return (ax > 1.0) ? 0.0 : Double.PositiveInfinity;
            }
            if (x == 0.0)
            {
                bool xNeg = Double.IsNegative(x);
                bool odd = Double.IsOddInteger(y);

                if (y > 0.0)
                {
                    return (odd && xNeg) ? -0.0 : 0.0;
                }
                else
                {
                    if (odd)
                        return xNeg ? Double.NegativeInfinity : Double.PositiveInfinity;
                    return Double.PositiveInfinity;
                }
            }

            if (Double.IsInfinity(x))
            {
                if (!Double.IsInteger(y))
                {
                    return (x > 0.0)
                        ? ((y > 0.0) ? Double.PositiveInfinity : 0.0)
                        : Double.NaN;
                }

                if (TryGetInt64FromIntegralDouble(y, out long yn))
                    return PowInteger(x, yn);

                return (y > 0.0) ? Double.PositiveInfinity : 0.0;
            }

            if (TryGetInt64FromIntegralDouble(y, out long n))
            {
                return PowInteger(x, n);
            }
            else if (Double.IsInteger(y))
            {
                double ax = (x < 0.0) ? -x : x;
                return Exp(y * Log(ax));
            }

            if (x < 0.0)
                return Double.NaN;

            // General case
            return Exp(y * Log(x));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double PowInteger(double x, long n)
        {
            if (n == 0)
                return 1.0;

            bool negExp = n < 0;

            ulong e = negExp ? (ulong)(-(n + 1)) + 1UL : (ulong)n;

            double result = 1.0;
            double baseVal = x;

            while (e != 0)
            {
                if ((e & 1UL) != 0)
                    result *= baseVal;

                baseVal *= baseVal;
                e >>= 1;
            }

            return negExp ? (1.0 / result) : result;
        }

        private static bool TryGetInt64FromIntegralDouble(double value, out long result)
        {
            result = 0;

            if (!Double.IsFinite(value))
                return false;

            if (value == 0.0)
                return true;

            ulong bits = BitConverter.DoubleToUInt64Bits(value);

            bool neg = (bits & 0x8000_0000_0000_0000UL) != 0;
            ulong absBits = bits & 0x7FFF_FFFF_FFFF_FFFFUL;

            int bexp = (int)((absBits >> 52) & 0x7FF);
            if (bexp == 0)
                return false;

            int exp = bexp - 1023;
            if (exp < 0)
                return false;

            // Check fractional part
            ulong mantOnly = absBits & 0x000F_FFFF_FFFF_FFFFUL;
            if (exp < 52)
            {
                ulong fracMask = (1UL << (52 - exp)) - 1UL;
                if ((mantOnly & fracMask) != 0)
                    return false;
            }

            if (exp > 63)
                return false;

            ulong mant = mantOnly | (1UL << 52); // implicit leading 1

            ulong intVal;
            if (exp >= 52)
                intVal = mant << (exp - 52);
            else
                intVal = mant >> (52 - exp);

            if (!neg)
            {
                if (intVal > 0x7FFF_FFFF_FFFF_FFFFUL)
                    return false;
                result = (long)intVal;
                return true;
            }
            else
            {
                // allow exactly 2^63 => long.MinValue
                if (intVal == 0x8000_0000_0000_0000UL)
                {
                    result = unchecked((long)0x8000_0000_0000_0000UL);
                    return true;
                }
                if (intVal > 0x7FFF_FFFF_FFFF_FFFFUL)
                    return false;

                result = -(long)intVal;
                return true;
            }
        }

        private const double ExpOverflowThreshold = 709.782712893384;   // ~ln(Double.MaxValue)
        private const double ExpUnderflowThreshold = -745.133219101941; // ~ln(Double.MinSubnormal)

        private static double Exp(double x)
        {
            if (Double.IsNaN(x))
                return Double.NaN;

            if (x == Double.PositiveInfinity)
                return Double.PositiveInfinity;
            if (x == Double.NegativeInfinity)
                return 0.0;

            if (x >= ExpOverflowThreshold)
                return Double.PositiveInfinity;
            if (x <= ExpUnderflowThreshold)
                return 0.0;

            // x = k*ln2 + r, r in ~[-ln2/2, ln2/2]
            double kReal = x / Ln2;

            int k = (int)kReal;
            double frac = kReal - (double)k;
            if (kReal >= 0.0)
            {
                if (frac > 0.5) k++;
            }
            else
            {
                if (-frac > 0.5) k--;
            }

            double r = x - (double)k * Ln2;

            double r2 = r * r;

            // 1 + r + r^2/2 + r^3/6 + ... + r^10/10!
            double p =
                1.0 +
                r * (1.0 +
                r * (0.5 +
                r * (0.16666666666666666 +
                r * (0.041666666666666664 +
                r * (0.008333333333333333 +
                r * (0.001388888888888889 +
                r * (0.0001984126984126984 +
                r * (0.0000248015873015873 +
                r * (0.0000027557319223985893 +
                r * (0.0000002755731922398589))))))))));

            return Pow2(k) * p;
        }

        private static double Pow2(int k)
        {
            if (k > 1023)
                return Double.PositiveInfinity;
            if (k < -1074)
                return 0.0;

            if (k >= -1022)
            {
                ulong bits = (ulong)(k + 1023) << 52;
                return BitConverter.UInt64BitsToDouble(bits);
            }
            else
            {
                // subnormal
                int shift = k + 1074; // 0..51
                ulong mant = 1UL << shift;
                return BitConverter.UInt64BitsToDouble(mant);
            }
        }

        private static double Log(double x)
        {
            if (Double.IsNaN(x))
                return Double.NaN;

            if (x == 0.0)
                return Double.NegativeInfinity;

            if (x < 0.0)
                return Double.NaN;

            if (x == Double.PositiveInfinity)
                return Double.PositiveInfinity;

            // Decompose x = m * 2^e with m in [1,2)
            ulong bits = BitConverter.DoubleToUInt64Bits(x);
            int bexp = (int)((bits >> 52) & 0x7FF);
            ulong mant = bits & 0x000F_FFFF_FFFF_FFFFUL;

            int e;
            if (bexp == 0)
            {
                const double TwoPow52 = 4503599627370496.0; // 2^52
                x *= TwoPow52;

                bits = BitConverter.DoubleToUInt64Bits(x);
                bexp = (int)((bits >> 52) & 0x7FF);
                mant = bits & 0x000F_FFFF_FFFF_FFFFUL;

                e = (bexp - 1023) - 52;
            }
            else
            {
                e = bexp - 1023;
            }

            // normalize mantissa to [1,2)
            double m = BitConverter.UInt64BitsToDouble(mant | 0x3FF0_0000_0000_0000UL);

            // ln(m) = 2 * (t + t^3/3 + t^5/5 + ...), t = (m-1)/(m+1)
            double t = (m - 1.0) / (m + 1.0);
            double t2 = t * t;

            double s = t;

            double term = t;
            term *= t2; s += term / 3.0;
            term *= t2; s += term / 5.0;
            term *= t2; s += term / 7.0;
            term *= t2; s += term / 9.0;
            term *= t2; s += term / 11.0;

            double ln_m = 2.0 * s;

            return (double)e * Ln2 + ln_m;
        }
    }

    // delegates

    public delegate bool Predicate<in T>(T obj)
        where T : allows ref struct;

    public delegate TResult Func<out TResult>()
        where TResult : allows ref struct;

    public delegate TResult Func<in T, out TResult>(T arg)
        where T : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, out TResult>(T1 arg1, T2 arg2)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, out TResult>(T1 arg1, T2 arg2, T3 arg3)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct
        where TResult : allows ref struct;

    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, in T16, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct
        where T16 : allows ref struct
        where TResult : allows ref struct;

    public delegate void Action();

    public delegate void Action<in T>(T obj)
        where T : allows ref struct;

    public delegate void Action<in T1, in T2>(T1 arg1, T2 arg2)
        where T1 : allows ref struct
        where T2 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3>(T1 arg1, T2 arg2, T3 arg3)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct;

    public delegate void Action<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, in T9, in T10, in T11, in T12, in T13, in T14, in T15, in T16>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16)
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct
        where T16 : allows ref struct;


    public static class GC
    {
        private enum StartNoGCRegionStatus
        {
            Succeeded,
            NotEnoughMemory,
            AmountTooLarge,
            AlreadyInProgress
        }

        private enum EndNoGCRegionStatus
        {
            Succeeded,
            NotInProgress,
            GCInduced,
            AllocationExceeded
        }


        /// <summary>
        /// Allocate an array while skipping zero-initialization if possible.
        /// </summary>
        /// <typeparam name="T">Specifies the type of the array element.</typeparam>
        /// <param name="length">Specifies the length of the array.</param>
        /// <param name="pinned">Specifies whether the allocated array must be pinned.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // forced to ensure no perf drop for small memory buffers (hot path)
        public static unsafe T[] AllocateUninitializedArray<T>(int length, bool pinned = false)
        {
            if (!pinned)
            {
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    return new T[length];
                }
            }

            return AllocateNewUninitializedArray(length, pinned);

            static T[] AllocateNewUninitializedArray(int length, bool pinned)
            {
                Internal.Runtime.GC_ALLOC_FLAGS flags = Internal.Runtime.GC_ALLOC_FLAGS.GC_ALLOC_ZEROING_OPTIONAL;
                if (pinned)
                    flags |= Internal.Runtime.GC_ALLOC_FLAGS.GC_ALLOC_PINNED_OBJECT_HEAP;
                if (length < 0)
                    throw new OverflowException();

                T[]? array = System.Runtime.RuntimeImports.RhAllocateNewArray<T>(length, (uint)flags);
                if (array == null)
                    throw new OutOfMemoryException();

                return array;
            }
        }
    }


    public enum PlatformID
    {
        Win32S = 0,
        Win32Windows = 1,
        Win32NT = 2,
        WinCE = 3,
        Unix = 4,
        Xbox = 5,
        MacOSX = 6,
        Other = 7
    }

    public sealed class Version
    {
        private readonly int _Major; // Do not rename
        private readonly int _Minor; // Do not rename
        private readonly int _Build; // Do not rename
        private readonly int _Revision; // Do not rename

        public Version(int major, int minor, int build, int revision)
        {
            if (major < 0 || minor < 0 || build < 0 || revision < 0) throw new ArgumentOutOfRangeException();
            _Major = major;
            _Minor = minor;
            _Build = build;
            _Revision = revision;
        }

        public Version(int major, int minor, int build)
        {
            if (major < 0 || minor < 0 || build < 0) throw new ArgumentOutOfRangeException();
            _Major = major;
            _Minor = minor;
            _Build = build;
            _Revision = -1;
        }

        public Version(int major, int minor)
        {
            if (major < 0 || minor < 0) throw new ArgumentOutOfRangeException();
            _Major = major;
            _Minor = minor;
            _Build = -1;
            _Revision = -1;
        }

        public Version()
        {
            //_Major = 0;
            //_Minor = 0;
            _Build = -1;
            _Revision = -1;
        }

        private Version(Version version)
        {
            _Major = version._Major;
            _Minor = version._Minor;
            _Build = version._Build;
            _Revision = version._Revision;
        }

        public int Major => _Major;

        public int Minor => _Minor;

        public int Build => _Build;

        public int Revision => _Revision;

        public short MajorRevision => (short)(_Revision >> 16);

        public short MinorRevision => (short)(_Revision & 0xFFFF);

        public int CompareTo(object? version)
        {
            if (version == null)
            {
                return 1;
            }

            if (version is Version v)
            {
                return CompareTo(v);
            }

            throw new ArgumentException();
        }

        public int CompareTo(Version? value)
        {
            return
                ReferenceEquals(value, this) ? 0 :
                value is null ? 1 :
                _Major != value._Major ? (_Major > value._Major ? 1 : -1) :
                _Minor != value._Minor ? (_Minor > value._Minor ? 1 : -1) :
                _Build != value._Build ? (_Build > value._Build ? 1 : -1) :
                _Revision != value._Revision ? (_Revision > value._Revision ? 1 : -1) :
                0;
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return Equals(obj as Version);
        }

        public bool Equals([NotNullWhen(true)] Version? obj)
        {
            return ReferenceEquals(obj, this) ||
                (obj is not null &&
                _Major == obj._Major &&
                _Minor == obj._Minor &&
                _Build == obj._Build &&
                _Revision == obj._Revision);
        }

        public override int GetHashCode()
        {
            // Let's assume that most version numbers will be pretty small and just
            // OR some lower order bits together.

            int accumulator = 0;

            accumulator |= (_Major & 0x0000000F) << 28;
            accumulator |= (_Minor & 0x000000FF) << 20;
            accumulator |= (_Build & 0x000000FF) << 12;
            accumulator |= (_Revision & 0x00000FFF);

            return accumulator;
        }

        public bool TryFormat(Span<char> destination, int fieldCount, out int charsWritten) =>
            TryFormatCore(destination, fieldCount, out charsWritten);

        private bool TryFormatCore<TChar>(Span<TChar> destination, int fieldCount, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            switch ((uint)fieldCount)
            {
                case > 4:
                    ThrowArgumentException("4");
                    break;

                case >= 3 when _Build == -1:
                    ThrowArgumentException("2");
                    break;

                case 4 when _Revision == -1:
                    ThrowArgumentException("3");
                    break;

                    static void ThrowArgumentException(string failureUpperBound) =>
                        throw new ArgumentException(failureUpperBound, nameof(fieldCount));
            }

            int totalCharsWritten = 0;

            for (int i = 0; i < fieldCount; i++)
            {
                if (i != 0)
                {
                    if (destination.IsEmpty)
                    {
                        charsWritten = 0;
                        return false;
                    }

                    destination[0] = TChar.CastFrom('.');
                    destination = destination.Slice(1);
                    totalCharsWritten++;
                }
                int value = i switch
                {
                    0 => _Major,
                    1 => _Minor,
                    2 => _Build,
                    _ => _Revision
                };
                int valueCharsWritten;
                bool formatted = typeof(TChar) == typeof(char) ?
                    ((uint)value).TryFormat(Unsafe.BitCast<Span<TChar>, Span<char>>(destination), out valueCharsWritten) :
                    ((uint)value).TryFormat(Unsafe.BitCast<Span<TChar>, Span<byte>>(destination), out valueCharsWritten, 
                        default, System.Globalization.CultureInfo.InvariantCulture);

                totalCharsWritten += valueCharsWritten;
                destination = destination.Slice(valueCharsWritten);
            }

            charsWritten = totalCharsWritten;
            return true;
        }

        public override string ToString() =>
            ToString(DefaultFormatFieldCount);

        public unsafe string ToString(int fieldCount)
        {
            Span<char> dest = stackalloc char[(4 * Number.Int32NumberBufferLength) + 3]; // at most 4 Int32s and 3 periods
            bool success = TryFormat(dest, fieldCount, out int charsWritten);
            return dest.Slice(0, charsWritten).ToString();
        }

        private int DefaultFormatFieldCount =>
            _Build == -1 ? 2 :
            _Revision == -1 ? 3 :
            4;
    }

    public sealed class OperatingSystem
    {
        private readonly Version _version;
        private readonly string? _servicePack;
        private readonly PlatformID _platform;
        private string? _versionString;

        public PlatformID Platform => _platform;

        public string ServicePack => _servicePack ?? string.Empty;

        public Version Version => _version;

        public unsafe string VersionString
        {
            get
            {
                if (_versionString == null)
                {
                    string os;
                    switch (_platform)
                    {
                        case PlatformID.Win32S: os = "Microsoft Win32S "; break;
                        case PlatformID.Win32Windows:
                            os = (_version.Major > 4 || (_version.Major == 4 && _version.Minor > 0))
                                ? "Microsoft Windows 98 " : "Microsoft Windows 95 "; break;
                        case PlatformID.Win32NT: os = "Microsoft Windows NT "; break;
                        case PlatformID.WinCE: os = "Microsoft Windows CE "; break;
                        case PlatformID.Unix: os = "Unix "; break;
                        case PlatformID.Xbox: os = "Xbox "; break;
                        case PlatformID.MacOSX: os = "Mac OS X "; break;
                        case PlatformID.Other: os = "Other "; break;
                        default:
                            os = "<unknown> "; break;
                    }

                    Span<char> stackBuffer = stackalloc char[128];
                    _versionString = string.IsNullOrEmpty(_servicePack) ?
                        string.Create(null, stackBuffer, $"{os}{_version}") :
                        string.Create(null, stackBuffer, $"{os}{_version.ToString(3)} {_servicePack}");
                }

                return _versionString;
            }
        }


        private static bool IsOSVersionAtLeast(int major, int minor, int build, int revision)
        {
            Version current = Environment.OSVersion.Version;

            if (current.Major != major)
            {
                return current.Major > major;
            }
            if (current.Minor != minor)
            {
                return current.Minor > minor;
            }
            // Unspecified build component is to be treated as zero
            int currentBuild = current.Build < 0 ? 0 : current.Build;
            build = build < 0 ? 0 : build;
            if (currentBuild != build)
            {
                return currentBuild > build;
            }

            // Unspecified revision component is to be treated as zero
            int currentRevision = current.Revision < 0 ? 0 : current.Revision;
            revision = revision < 0 ? 0 : revision;

            return currentRevision >= revision;
        }
    }

    public enum TypeCode
    {
        Empty = 0,          // Null reference
        Object = 1,         // Instance that isn't a value
        DBNull = 2,         // Database null value
        Boolean = 3,        // Boolean
        Char = 4,           // Unicode character
        SByte = 5,          // Signed 8-bit integer
        Byte = 6,           // Unsigned 8-bit integer
        Int16 = 7,          // Signed 16-bit integer
        UInt16 = 8,         // Unsigned 16-bit integer
        Int32 = 9,          // Signed 32-bit integer
        UInt32 = 10,        // Unsigned 32-bit integer
        Int64 = 11,         // Signed 64-bit integer
        UInt64 = 12,        // Unsigned 64-bit integer
        Single = 13,        // IEEE 32-bit float
        Double = 14,        // IEEE 64-bit double
        Decimal = 15,       // Decimal
        DateTime = 16,      // DateTime
        String = 18,        // Unicode character string
    }

    public abstract class Type : System.Reflection.MemberInfo, System.Reflection.IReflect
    {
        protected Type() { }

        public abstract string? Namespace { get; }
        public abstract string? AssemblyQualifiedName { get; }
        public abstract string? FullName { get; }

        public abstract Type UnderlyingSystemType { get; }

        public virtual bool IsEnum { [Intrinsic] get => throw new NotImplementedException(); }
        public bool IsPrimitive
        {
            [Intrinsic]
            get => IsPrimitiveImpl();
        }
        protected abstract bool IsPrimitiveImpl();
        public bool IsValueType
        {
            [Intrinsic]
            get => IsValueTypeImpl();
        }
        protected virtual bool IsValueTypeImpl() => throw new NotImplementedException();
    }

    public enum AttributeTargets
    {
        Assembly = 0x0001,
        Module = 0x0002,
        Class = 0x0004,
        Struct = 0x0008,
        Enum = 0x0010,
        Constructor = 0x0020,
        Method = 0x0040,
        Property = 0x0080,
        Field = 0x0100,
        Event = 0x0200,
        Interface = 0x0400,
        Parameter = 0x0800,
        Delegate = 0x1000,
        ReturnValue = 0x2000,
        GenericParameter = 0x4000,

        All = Assembly | Module | Class | Struct | Enum | Constructor |
                        Method | Property | Field | Event | Interface | Parameter |
                        Delegate | ReturnValue | GenericParameter
    }

    public class Exception
    {
        private string _message;
        public Exception()
        {
            _message = String.Empty;
        }
        public Exception(String message)
        {
            _message = message;
        }
        public Exception(String message, Exception innerException)
        {
            _message = message;
        }
        public virtual string Message => _message;
        public override string ToString()
        {
            return _message;
        }
    }
    public class ApplicationException : Exception
    {
        public ApplicationException()
            : base()
        { }
        public ApplicationException(string message)
            : base(message)
        { }
        public ApplicationException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class SystemException : Exception
    {
        public SystemException()
            : base()
        { }

        public SystemException(string message)
            : base(message)
        { }

        public SystemException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public sealed class TypeInitializationException : SystemException
    {
        private readonly string? _typeName;

        private TypeInitializationException()
            : base()
        { }


        public TypeInitializationException(string? fullTypeName, Exception? innerException)
            : this(fullTypeName, string.Empty, innerException)
        { }

        internal TypeInitializationException(string? message) : base(message ?? string.Empty)
        { }

        internal TypeInitializationException(string? fullTypeName, string? message, Exception? innerException)
            : base(message ?? string.Empty, innerException)
        {
            _typeName = fullTypeName;
        }

        public string TypeName => _typeName ?? string.Empty;
    }
    public class SerializationException : SystemException
    {
        private static string _nullMessage = "Arg_SerializationException";
        public SerializationException()
        : base(_nullMessage)
        { }
        public SerializationException(string message)
        : base(message)
        { }
        public SerializationException(string message, Exception innerException)
       : base(message, innerException)
        { }
    }
    public class InvalidCastException : SystemException
    {
        public InvalidCastException() : base("Specified cast is not valid.") { }
        public InvalidCastException(string message) : base(message) { }
        public InvalidCastException(string message, Exception innerException) : base(message, innerException) { }
    }
    public class FormatException : SystemException
    {
        public FormatException()
            : base() { }
        public FormatException(string message)
            : base(message) { }
        public FormatException(string message, Exception innerException)
            : base(message, innerException) { }
    }
    public class ArrayTypeMismatchException : SystemException
    {
        public ArrayTypeMismatchException() : base() { }
        public ArrayTypeMismatchException(string message) : base(message) { }
        public ArrayTypeMismatchException(string message, Exception innerException) : base(message, innerException) { }
    }
    public class AccessViolationException : SystemException
    {
        public AccessViolationException()
            : base()
        { }

        public AccessViolationException(string message)
            : base(message)
        { }

        public AccessViolationException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class OutOfMemoryException : SystemException
    {
        public OutOfMemoryException() : base(GetDefaultMessage())
        { }

        public OutOfMemoryException(string message)
            : base(message ?? GetDefaultMessage())
        { }

        public OutOfMemoryException(string message, Exception innerException)
            : base(message ?? GetDefaultMessage(), innerException)
        { }

        private static string GetDefaultMessage() => "Out of memory.";
    }
    public class NullReferenceException : SystemException
    {
        public NullReferenceException()
            : base()
        { }

        public NullReferenceException(string message)
            : base(message)
        { }

        public NullReferenceException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class InvalidOperationException : SystemException
    {
        public InvalidOperationException()
            : base()
        { }

        public InvalidOperationException(string message)
            : base(message)
        { }

        public InvalidOperationException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class NotImplementedException : SystemException
    {
        public NotImplementedException()
        : base("Arg_NotImplementedException")
        { }

        public NotImplementedException(string message)
        : base(message)
        { }

        public NotImplementedException(string message, Exception inner)
        : base(message, inner)
        { }
    }
    public class NotSupportedException : SystemException
    {
        public NotSupportedException()
            : base()
        { }

        public NotSupportedException(string message)
            : base(message)
        { }

        public NotSupportedException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class PlatformNotSupportedException : NotSupportedException
    {
        public PlatformNotSupportedException()
            : base()
        { }

        public PlatformNotSupportedException(string message)
            : base(message)
        { }

        public PlatformNotSupportedException(string message, Exception inner)
            : base(message, inner)
        { }
    }
    public class ArithmeticException : SystemException
    {
        public ArithmeticException()
            : base()
        { }

        public ArithmeticException(string message)
            : base(message)
        { }

        public ArithmeticException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class OverflowException : ArithmeticException
    {
        public OverflowException()
            : base("Arithmetic operation resulted in an overflow.")
        { }

        public OverflowException(string message)
            : base(message)
        { }

        public OverflowException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class DivideByZeroException : ArithmeticException
    {
        public DivideByZeroException()
            : base("Attempted to divide by zero.")
        { }

        public DivideByZeroException(string message)
            : base(message)
        { }

        public DivideByZeroException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
    public class ArgumentException : SystemException
    {
        private readonly string _paramName;
        public virtual string ParamName => _paramName;

        public ArgumentException()
            : base()
        { }

        public ArgumentException(string message)
            : base(message)
        { }

        public ArgumentException(string message, Exception innerException)
            : base(message, innerException)
        { }

        public ArgumentException(string message, string paramName, Exception innerException)
            : base(message, innerException)
        {
            _paramName = paramName;
        }

        public ArgumentException(string message, string paramName)
            : base(message)
        {
            _paramName = paramName;
        }
    }
    public class CultureNotFoundException : ArgumentException
    {
        private readonly string _invalidCultureName; // unrecognized culture name
        private readonly int _invalidCultureId;     // unrecognized culture Lcid

        public CultureNotFoundException()
            : base(DefaultMessage)
        {
        }

        public CultureNotFoundException(string message)
            : base(message ?? DefaultMessage)
        {
        }

        public CultureNotFoundException(string paramName, string message)
            : base(message ?? DefaultMessage, paramName)
        {
        }

        public CultureNotFoundException(string message, Exception innerException)
            : base(message ?? DefaultMessage, innerException)
        {
        }

        public CultureNotFoundException(string paramName, string invalidCultureName, string message)
            : base(message ?? DefaultMessage, paramName)
        {
            _invalidCultureName = invalidCultureName;
        }

        public CultureNotFoundException(string message, string invalidCultureName, Exception innerException)
            : base(message ?? DefaultMessage, innerException)
        {
            _invalidCultureName = invalidCultureName;
        }

        public CultureNotFoundException(string message, int invalidCultureId, Exception innerException)
            : base(message ?? DefaultMessage, innerException)
        {
            _invalidCultureId = invalidCultureId;
        }

        public CultureNotFoundException(string paramName, int invalidCultureId, string message)
            : base(message ?? DefaultMessage, paramName)
        {
            _invalidCultureId = invalidCultureId;
        }

        public virtual int InvalidCultureId => _invalidCultureId;

        public virtual string InvalidCultureName => _invalidCultureName;

        private static string DefaultMessage => "Culture not supported.";
    }
    public class ArgumentNullException : ArgumentException
    {
        public ArgumentNullException() : base("Value cannot be null.") { }
        public ArgumentNullException(string paramName) : base("Value cannot be null.", paramName) { }
        public ArgumentNullException(string message, string paramName) : base(message, paramName) { }
    }

    public class ArgumentOutOfRangeException : ArgumentException
    {
        public ArgumentOutOfRangeException() : base("Value is out of range.") { }
        public ArgumentOutOfRangeException(string paramName) : base("Value is out of range.", paramName) { }
        public ArgumentOutOfRangeException(string message, string paramName) : base(message, paramName) { }
    }

    public class IndexOutOfRangeException : SystemException
    {
        public IndexOutOfRangeException() : base("Index was outside the bounds of the array.") { }
        public IndexOutOfRangeException(string message) : base(message) { }
        public IndexOutOfRangeException(string message, Exception inner) : base(message, inner) { }
    }

    public abstract class Attribute
    {

    }

    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = true)]
    public sealed class AttributeUsageAttribute : Attribute
    {
        public AttributeUsageAttribute(AttributeTargets validOn)
        {
            ValidOn = validOn;
            Inherited = true;
        }

        public AttributeTargets ValidOn { get; }

        public bool AllowMultiple { get; set; }

        public bool Inherited { get; set; }
    }
    /// <summary>
    /// Indicates that the value of a static field is unique for each thread.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public class ThreadStaticAttribute : Attribute
    {
        public ThreadStaticAttribute()
        {
        }
    }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class OverloadResolutionPriorityAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OverloadResolutionPriorityAttribute"/> class.
        /// </summary>
        public OverloadResolutionPriorityAttribute(int priority)
        {
            Priority = priority;
        }

        /// <summary>
        /// The priority of the member.
        /// </summary>
        public int Priority { get; }
    }
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class AllowNullAttribute : Attribute
    {
        public AllowNullAttribute()
        { }
    }
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class NotNullAttribute : Attribute
    {
        public NotNullAttribute()
        { }
    }
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

        public bool ReturnValue { get; }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = false)]
    public sealed class CLSCompliantAttribute : Attribute
    {
        private readonly bool _compliant;

        public CLSCompliantAttribute(bool isCompliant)
        {
            _compliant = isCompliant;
        }
        public bool IsCompliant => _compliant;
    }
    [AttributeUsage(AttributeTargets.Enum, Inherited = false)]
    public class FlagsAttribute : Attribute
    {
        public FlagsAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, Inherited = false)]
    public sealed class IntrinsicAttribute : Attribute
    {
        public IntrinsicAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, Inherited = false)]
    public sealed class NonVersionableAttribute : Attribute
    {
        public NonVersionableAttribute() { }
    }
    public class Random
    {
        private int _seed;
        public static Random Shared { get; } = new ThreadSafeRandom();
        public Random() { }
        public Random(int seed) { _seed = seed; }


        public virtual int Next() => Next(0, 0x7fffffff);
        public virtual int Next(int maxValue) => Next(0, maxValue);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public virtual int Next(int minValue, int maxValue)
        {
            if (minValue > maxValue)
            {
                throw new Exception();
            }

            //int result = _impl.Next(minValue, maxValue);
            //AssertInRange(result, minValue, maxValue);
            return -1;
        }
    }
    public class ThreadSafeRandom : Random
    {
        public ThreadSafeRandom() { }
    }

    public static class Console
    {
        private static ReadOnlySpan<char> _trueString => ['T', 'r', 'u', 'e', '\0'];
        private static ReadOnlySpan<char> _trueWithNewLineString => ['T', 'r', 'u', 'e', '\n', '\0'];
        private static ReadOnlySpan<char> _falseString => ['F', 'a', 'l', 's', 'e', '\0'];
        private static ReadOnlySpan<char> _falseWithNewLineString => ['F', 'a', 'l', 's', 'e', '\n', '\0'];
        private static ReadOnlySpan<char> _intMinValueString => ['-', '2', '1', '4', '7', '4', '8', '3', '6', '4', '8', '\0'];
        private static ReadOnlySpan<char> _intMinValueWithNewLineString => ['-', '2', '1', '4', '7', '4', '8', '3', '6', '4', '8', '\n', '\0'];
        private static ReadOnlySpan<char> _longMinValueString =>
            ['-', '9', '2', '2', '3', '3', '7', '2', '0', '3', '6', '8', '5', '4', '7', '7', '5', '8', '0', '8', '\0'];
        public static void Write(sbyte value) { Write((int)value); }
        public static void Write(byte value) { Write((int)value); }
        public static void Write(short value) { Write((int)value); }
        public static void Write(ushort value) { Write((int)value); }
        public static unsafe void Write(int value)
        {
            char* p = stackalloc char[12] + 11;
            *p = '\0';
            if (value == unchecked((int)0x80000000)) //int.MinValue
            {
                //-2147483648
                _Write((char*)Unsafe.AsPointer(in _intMinValueString._reference));
                return;
            }
            bool negative = value < 0;
            if (negative)
                value = -value;

            do
            {
                int digit = value % 10;
                value /= 10;
                *--p = (char)('0' + digit);
            }
            while (value != 0);
            if (negative)
                *--p = '-';

            _Write(p);
        }
        public static void Write(uint value) { Write((long)value); }
        public static unsafe void Write(long value)
        {
            char* p = stackalloc char[21] + 20;
            *p = '\0';

            if (value == unchecked((long)0x8000000000000000)) // long.MinValue
            {
                //-9223372036854775808
                _Write((char*)Unsafe.AsPointer(in _longMinValueString._reference));
                return;
            }
            bool negative = value < 0;
            if (negative)
                value = -value;

            do
            {
                long digit = value % 10;
                value /= 10;
                *--p = (char)('0' + digit);
            }
            while (value != 0);

            if (negative)
                *--p = '-';

            _Write(p);
        }
        public static unsafe void Write(ulong value)
        {
            char* p = stackalloc char[21] + 20; // 20 digits + terminator
            *p = '\0';

            do
            {
                ulong digit = value % 10ul;
                value /= 10ul;
                *--p = (char)('0' + digit);
            } while (value != 0ul);

            _Write(p);
        }
        public static unsafe void Write(float value)
        {
            char* buffer = stackalloc char[System.Number.FloatFormatBufferCharCount + 1];
            int length = System.Number.FormatFloatToBuffer(value, null, null, buffer, System.Number.FloatFormatBufferCharCount);
            buffer[length] = '\0';
            _Write(buffer);
        }
        public static unsafe void Write(double value)
        {
            char* buffer = stackalloc char[System.Number.DoubleFormatBufferCharCount + 1];
            int length = System.Number.FormatDoubleToBuffer(value, null, null, buffer, System.Number.DoubleFormatBufferCharCount);
            buffer[length] = '\0';
            _Write(buffer);
        }
        public static unsafe void Write(char value) { uint terminated = value; _Write((char*)&terminated); }
        public static unsafe void Write(bool value)
        {
            _Write((char*)Unsafe.AsPointer(in (value ? ref _trueString._reference : ref _falseString._reference)));
        }
        public static unsafe void Write(char* value) { _Write(value); }
        public static void Write(ReadOnlySpan<char> value) { _Write(value); }
        public static void Write(string value) { _Write(value); }
        public static void Write(object value) { _Write(value.ToString()); }

        public unsafe static void WriteLine() { uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(sbyte value) { WriteLine((int)value); }
        public unsafe static void WriteLine(byte value) { WriteLine((int)value); }
        public unsafe static void WriteLine(short value) { WriteLine((int)value); }
        public unsafe static void WriteLine(ushort value) { WriteLine((int)value); }
        public unsafe static void WriteLine(int value)
        {
            char* p = stackalloc char[13] + 11;
            *(p + 1) = '\0';
            *p = '\n';
            if (value == unchecked((int)0x80000000)) //int.MinValue
            {
                //-2147483648
                _Write((char*)Unsafe.AsPointer(in _intMinValueWithNewLineString._reference));
                return;
            }
            bool negative = value < 0;
            if (negative)
                value = -value;

            do
            {
                int digit = value % 10;
                value /= 10;
                *--p = (char)('0' + digit);
            }
            while (value != 0);
            if (negative)
                *--p = '-';

            _Write(p);
        }
        public unsafe static void WriteLine(uint value) { Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(long value) { Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(ulong value) { Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(char value) { ulong s = (ulong)value | ((ulong)'\n' << 16); _Write((char*)&s); }
        public unsafe static void WriteLine(bool value)
        {
            _Write((char*)Unsafe.AsPointer(in (value ? ref _trueWithNewLineString._reference : ref _falseWithNewLineString._reference)));
        }
        public unsafe static void WriteLine(float value) { Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(double value) { Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(string value) { _Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(ReadOnlySpan<char> value) { _Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static unsafe void WriteLine(char* value) { _Write(value); uint nl = '\n'; _Write((char*)&nl); }
        public unsafe static void WriteLine(object value)
        {
            if (value != null)
            {
                _Write(value.ToString());
            }
            uint nl = '\n'; _Write((char*)&nl);
        }
        // intrinsics
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static unsafe void _Write(char* value) { }
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static void _Write(string value) { }
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static void _Write(ReadOnlySpan<char> value) { }
    }

    public unsafe class Buffer
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Memmove<T>(ref T dest, ref T src, nuint len)
        {
            if (len == (nuint)0)
                return;

            if (System.Runtime.CompilerServices.Unsafe.AreSame<T>(ref dest, ref src))
                return;

            int n = (int)len;
            if ((nuint)n != len)
                throw new ArgumentOutOfRangeException("len");

            nuint elemSize = (nuint)System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            nuint byteLen = elemSize * len;

            nint byteOffset = System.Runtime.CompilerServices.Unsafe.ByteOffset<T>(ref src, ref dest); // dest - src
            bool copyBackwards = (byteOffset > 0) && ((nuint)byteOffset < byteLen);

            if (!copyBackwards)
            {
                for (int i = 0; i < n; i++)
                {
                    System.Runtime.CompilerServices.Unsafe.Add<T>(ref dest, i) =
                        System.Runtime.CompilerServices.Unsafe.Add<T>(ref src, i);
                }
            }
            else
            {
                for (int i = n - 1; i >= 0; i--)
                {
                    System.Runtime.CompilerServices.Unsafe.Add<T>(ref dest, i) =
                        System.Runtime.CompilerServices.Unsafe.Add<T>(ref src, i);
                }
            }
        }
    }

}
