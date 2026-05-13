namespace System
{
    public static class Environment
    {
        internal const int SystemTarget = 32;
        internal const bool Target64 = SystemTarget == 64;
        internal const bool Target32 = SystemTarget == 32;
        internal const bool IsDebug = false;
        internal const bool IsRelease = !IsDebug;
        internal const string NewLineConst = "\n";
        public static string NewLine => NewLineConst;
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
        public const string Empty = "";
        public int Length
        {
            [MethodImpl(MethodImplOptions.InternalCall)]
            get { return 0; }
        }
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal ref char GetRawStringData() { throw new NullReferenceException(); }
        [MethodImpl(MethodImplOptions.InternalCall)]
        public ref char GetPinnableReference() { throw new NullReferenceException(); }
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
        public unsafe String(char* value)
        {
            if (value == null) throw new ArgumentNullException("value");

            ref char dst = ref GetRawStringData();
            for (int i = 0; i < Length; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = value[i];
        }

        public char this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Length)
                    throw new IndexOutOfRangeException();
                ref char r0 = ref GetPinnableReference();
                return System.Runtime.CompilerServices.Unsafe.Add<char>(ref r0, index);
            }
        }

        public override string ToString() => this;
        public override bool Equals(object obj)
        {
            var s = obj as string;
            if (s == null) return false;
            return Equals(s);
        }
        public bool Equals(string other)
        {
            if ((object)other == null) return false;
            if ((object)this == (object)other) return true;
            int n = Length;
            if (n != other.Length) return false;
            ref char a = ref GetPinnableReference();
            ref char b = ref other.GetPinnableReference();
            for (int i = 0; i < n; i++)
            {
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref a, i) !=
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref b, i))
                    return false;
            }
            return true;
        }
        public override int GetHashCode()
        {
            uint hash = 2166136261u;
            ref char p = ref GetPinnableReference();
            for (int i = 0; i < Length; i++)
            {
                hash ^= (uint)System.Runtime.CompilerServices.Unsafe.Add<char>(ref p, i);
                hash *= 16777619u;
            }
            return unchecked((int)hash);
        }
        public static bool operator ==(string left, string right)
        {
            if ((object)left == (object)right) return true;
            if ((object)left == null || (object)right == null) return false;
            return left.Equals(right);
        }
        public static bool operator !=(string left, string right) => !(left == right);
        public static bool IsNullOrEmpty(string value) => (object)value == null || value.Length == 0;
        public static bool IsNullOrWhiteSpace(string value)
        {
            if ((object)value == null) return true;
            int n = value.Length;
            if (n == 0) return true;

            ref char p = ref value.GetPinnableReference();
            for (int i = 0; i < n; i++)
            {
                if (!Char.IsWhiteSpace(System.Runtime.CompilerServices.Unsafe.Add<char>(ref p, i)))
                    return false;
            }
            return true;
        }
        public ReadOnlySpan<char> AsSpan()
        {
            ref char r = ref GetPinnableReference();
            return new ReadOnlySpan<char>(ref r, Length);
        }
        public ReadOnlySpan<char> AsSpan(int start)
        {
            int len = Length;
            if ((uint)start > (uint)len)
                throw new ArgumentOutOfRangeException("start");

            ref char r = ref GetPinnableReference();
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

            ref char r = ref GetPinnableReference();
            return new ReadOnlySpan<char>(
                ref System.Runtime.CompilerServices.Unsafe.Add<char>(ref r, start),
                length);
        }
        public static implicit operator ReadOnlySpan<char>(String? value)
        {
            ref char r = ref value.GetPinnableReference();
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
            ref char src = ref GetPinnableReference();

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
            ref char src = ref GetPinnableReference();

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

            ref char a = ref GetPinnableReference();
            ref char b = ref value.GetPinnableReference();

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

            ref char src = ref GetPinnableReference();
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

            ref char src = ref GetPinnableReference();
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

            ref char a = ref GetPinnableReference();
            ref char b = ref value.GetPinnableReference();

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

            ref char a = ref GetPinnableReference();
            ref char b = ref value.GetPinnableReference();
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

            ref char a = ref GetPinnableReference();
            ref char b = ref value.GetPinnableReference();
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
            ref char src = ref GetPinnableReference();
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

            ref char src = ref GetPinnableReference();
            ref char ov = ref oldValue.GetPinnableReference();

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
            ref char src = ref GetPinnableReference();
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

            ref char src = ref srcStr.GetPinnableReference();
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
                    ref char src = ref s.GetPinnableReference();
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
                ref char src = ref a.GetPinnableReference();
                for (int i = 0; i < la; i++)
                    System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) =
                        System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i);
                pos = la;
            }
            if (lb != 0)
            {
                ref char src = ref b.GetPinnableReference();
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

            ref char src = ref source.GetPinnableReference();

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

            ref char src = ref GetPinnableReference();
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

            ref char src = ref GetPinnableReference();
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

        private static ReadOnlySpan<byte> Latin1CharInfo => new byte[]
        {
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
        };

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
    }

    public struct SByte
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
    public struct Byte
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
    public struct Int16
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
            return System.Number.Int32ToString((int)m_value);
        }
    }
    public struct UInt16
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
    }
    public struct Int32
    {
        private readonly int m_value;
        public const int MaxValue = 0x7fffffff;
        public const int MinValue = unchecked((int)0x80000000);

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
            return System.Number.Int32ToString(m_value);
        }
    }
    public struct UInt32
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
            return System.Number.UInt32ToString(m_value);
        }
    }
    public struct Int64
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
            return System.Number.Int64ToString(m_value, null);
        }
        public string ToString(IFormatProvider provider)
        {
            return System.Number.Int64ToString(m_value, null);
        }
        public string ToString(string format)
        {
            return System.Number.Int64ToString(m_value, format);
        }
    }
    public struct UInt64
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
            return System.Number.UInt64ToString(m_value);
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
            return System.Number.SingleToString(m_value);
        }
        public string ToString(System.Globalization.CultureInfo cultureInfo)
        {
            return System.Number.SingleToString(m_value);
        }
        public string ToString(string format, System.Globalization.CultureInfo cultureInfo)
        {
            return System.Number.SingleToString(m_value);
        }
        public static float Abs(float value) => MathF.Abs(value);

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
            return System.Number.DoubleToString(m_value);
        }
        public string ToString(System.Globalization.CultureInfo cultureInfo)
        {
            return System.Number.DoubleToString(m_value);
        }
        public string ToString(string format, System.Globalization.CultureInfo cultureInfo)
        {
            return System.Number.DoubleToString(m_value);
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
            return System.Number.DoubleToString((double)m_value);
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

    internal static unsafe class Number
    {
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
        internal static unsafe string Int32ToString(int value, string format = null)
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
        internal static unsafe string UInt32ToString(uint value, string format = null)
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
        internal static unsafe string Int64ToString(long value, string format = null)
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
        internal static unsafe string UInt64ToString(ulong value, string format = null)
        {
            char* buffer = stackalloc char[21]; // 20 digits + terminator
            char* p = buffer + 21;

            ulong v = value;
            do
            {
                ulong digit = v % 10ul;
                v /= 10ul;
                *--p = (char)('0' + digit);
            } while (v != 0ul);

            int len = (int)((buffer + 21) - p);
            string s = String.FastAllocateString(len);
            ref char dst = ref s.GetRawStringData();

            for (int i = 0; i < len; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = p[i];

            return s;
        }

        internal static string SingleToString(float value)
        {
            uint bits = BitConverter.SingleToUInt32Bits(value);
            bool negative = (bits & 0x8000_0000U) != 0;
            uint absBits = bits & 0x7FFF_FFFFU;

            if ((absBits & 0x7F80_0000U) == 0x7F80_0000U)
            {
                if ((absBits & 0x007F_FFFFU) != 0)
                    return "NaN";

                return negative ? "-Infinity" : "Infinity";
            }

            if (absBits == 0)
                return "0";

            if (value > -2147483648.0f && value < 2147483648.0f)
            {
                int integerValue = (int)value;
                if ((float)integerValue == value)
                    return Int32ToString(integerValue);
            }

            return DoubleToString((double)value);
        }

        internal static unsafe string DoubleToString(double value)
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
                    return "NaN";

                return negative ? "-Infinity" : "Infinity";
            }

            if (absBits == 0)
                return "0";

            if (value > -9223372036854775808.0 && value < 9223372036854775808.0)
            {
                long integerValue = (long)value;
                if ((double)integerValue == value)
                    return Int64ToString(integerValue, null);
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

            return FormatShortestDouble(negative, digits, decimalScale);
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
        private static ulong ComputeRoundedScaledDigits(ulong mantissa, int binaryExponent, int decimalScale)
        {
            uint[] numerator = UIntArrayFromUInt64(mantissa);
            uint[] denominator = UIntArrayFromUInt64(1UL);

            if (binaryExponent >= 0)
                numerator = ShiftLeft(numerator, binaryExponent);
            else
                denominator = ShiftLeft(denominator, -binaryExponent);

            if (decimalScale >= 0)
                denominator = MultiplyPow10(denominator, decimalScale);
            else
                numerator = MultiplyPow10(numerator, -decimalScale);

            return RoundQuotientToUInt64(numerator, denominator);
        }
        private static ulong RoundQuotientToUInt64(uint[] numerator, uint[] denominator)
        {
            uint[] remainder;
            uint[] quotient = System.Numerics.BigIntegerCalculator.Divide(numerator, denominator, out remainder);

            uint[] twiceRemainder = ShiftLeft(remainder, 1);
            int cmp = System.Numerics.BigIntegerCalculator.Compare(twiceRemainder, denominator);
            ulong result = UIntArrayToUInt64(quotient);

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
        private static int ComparePositiveBinaryFloatToPowerOf10(ulong mantissa, int binaryExponent, int decimalExponent)
        {
            uint[] left = UIntArrayFromUInt64(mantissa);
            uint[] right = UIntArrayFromUInt64(1UL);

            if (decimalExponent >= 0)
            {
                if (binaryExponent >= 0)
                {
                    left = ShiftLeft(left, binaryExponent);
                    right = Pow10UInt(decimalExponent);
                }
                else
                {
                    right = MultiplyPow10(right, decimalExponent);
                    right = ShiftLeft(right, -binaryExponent);
                }
            }
            else
            {
                left = MultiplyPow10(left, -decimalExponent);
                if (binaryExponent >= 0)
                    left = ShiftLeft(left, binaryExponent);
                else
                    right = ShiftLeft(right, -binaryExponent);
            }

            return System.Numerics.BigIntegerCalculator.Compare(left, right);
        }
        private static int CompareDecimalToBinary(ulong decimalDigits, int decimalScale, ulong binaryMantissa, int binaryExponent)
        {
            uint[] left = UIntArrayFromUInt64(decimalDigits);
            uint[] right = UIntArrayFromUInt64(binaryMantissa);

            if (decimalScale >= 0)
                left = MultiplyPow10(left, decimalScale);
            else
                right = MultiplyPow10(right, -decimalScale);

            if (binaryExponent >= 0)
                right = ShiftLeft(right, binaryExponent);
            else
                left = ShiftLeft(left, -binaryExponent);

            return System.Numerics.BigIntegerCalculator.Compare(left, right);
        }
        private static uint[] UIntArrayFromUInt64(ulong value)
        {
            if (value == 0UL)
                return Array.Empty<uint>();

            uint lo = (uint)value;
            uint hi = (uint)(value >> 32);

            if (hi == 0U)
                return new uint[] { lo };

            return new uint[] { lo, hi };
        }
        private static ulong UIntArrayToUInt64(uint[] value)
        {
            int length = UIntArrayLength(value);
            if (length == 0)
                return 0UL;
            if (length == 1)
                return value[0];
            if (length == 2)
                return ((ulong)value[1] << 32) | value[0];

            throw new OverflowException();
        }
        private static int UIntArrayLength(uint[] value)
        {
            int length = value.Length;
            while (length > 0 && value[length - 1] == 0U)
                length--;
            return length;
        }
        private static uint[] Pow10UInt(int exponent)
        {
            uint[] result = UIntArrayFromUInt64(1UL);
            return MultiplyPow10(result, exponent);
        }
        private static uint[] MultiplyPow10(uint[] value, int exponent)
        {
            uint[] result = value;
            for (int i = 0; i < exponent; i++)
                result = MultiplyByUInt32(result, 10U);
            return result;
        }
        private static uint[] MultiplyByUInt32(uint[] value, uint multiplier)
        {
            int length = UIntArrayLength(value);
            if (length == 0 || multiplier == 0U)
                return Array.Empty<uint>();

            uint[] result = new uint[length + 1];
            ulong carry = 0UL;

            for (int i = 0; i < length; i++)
            {
                ulong product = (ulong)value[i] * multiplier + carry;
                result[i] = (uint)product;
                carry = product >> 32;
            }

            result[length] = (uint)carry;
            return result;
        }
        private static uint[] ShiftLeft(uint[] value, int shift)
        {
            if (shift == 0 || UIntArrayLength(value) == 0)
                return value;

            return System.Numerics.BigIntegerCalculator.ShiftLeft(value, shift);
        }
        private static string FormatShortestDouble(bool negative, ulong digits, int decimalScale)
        {
            string digitText = UInt64ToString(digits);
            int digitCount = digitText.Length;
            int scientificExponent = digitCount + decimalScale - 1;

            string body;
            if (scientificExponent >= -4 && scientificExponent < digitCount)
                body = FormatFixedDecimal(digitText, decimalScale);
            else
                body = FormatScientificDecimal(digitText, scientificExponent);

            return negative ? ("-" + body) : body;
        }
        private static string FormatFixedDecimal(string digits, int decimalScale)
        {
            int decimalPoint = digits.Length + decimalScale;

            if (decimalPoint <= 0)
                return "0." + RepeatChar('0', -decimalPoint) + digits;

            if (decimalPoint >= digits.Length)
                return digits + RepeatChar('0', decimalPoint - digits.Length);

            return digits.Substring(0, decimalPoint) + "." + digits.Substring(decimalPoint);
        }
        private static string FormatScientificDecimal(string digits, int scientificExponent)
        {
            string significand;
            if (digits.Length == 1)
                significand = digits;
            else
                significand = CharToString(digits[0]) + "." + digits.Substring(1);

            return significand + FormatExponent(scientificExponent);
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
        private static string RepeatChar(char c, int count)
        {
            if (count <= 0)
                return string.Empty;

            string result = String.FastAllocateString(count);
            ref char dst = ref result.GetRawStringData();

            for (int i = 0; i < count; i++)
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) = c;

            return result;
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

    public struct ValueTuple
    {
        public override string ToString()
        {
            return "()";
        }
    }
    public struct ValueTuple<T1> : ITuple
    {
        public T1 Item1;

        public ValueTuple(T1 item1)
        {
            Item1 = item1;
        }
        int ITuple.Length => 1;
        object ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                _ => throw new IndexOutOfRangeException(),
            };
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
        object ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                _ => throw new IndexOutOfRangeException(),
            };
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
        object ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                _ => throw new IndexOutOfRangeException(),
            };
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
        object ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                3 => Item4,
                _ => throw new IndexOutOfRangeException(),
            };
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
        object ITuple.this[int index] =>
            index switch
            {
                0 => Item1,
                1 => Item2,
                2 => Item3,
                3 => Item4,
                4 => Item5,
                _ => throw new IndexOutOfRangeException(),
            };
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
        object ITuple.this[int index] =>
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
        object ITuple.this[int index] =>
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
        object ITuple.this[int index] =>
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
            if (Environment.SystemTarget == 64)
                _value = (nint)value;
            else
                _value = checked((nint)value);
        }

        public unsafe IntPtr(void* value)
        {
            _value = (nint)value;
        }

        public long ToInt64() => _value;


        public static int Size
        {
            get => Environment.SystemTarget / 8;
        }

        public static nint MaxValue
        {
            get => unchecked((nint)(Environment.Target64
                    ? 0x7fffffffffffffffL
                    : 0x7fffffff));
        }

        public static nint MinValue
        {
            get => unchecked((nint)(Environment.Target64
                    ? unchecked((long)0x8000000000000000L)
                    : unchecked((int)0x80000000)));
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => (obj is nint other) && Equals(other);
        public override int GetHashCode()
        {
            if (Environment.Target64) 
            {
                long value = _value;
                return value.GetHashCode();
            }
            else
            {
                return (int)_value;
            }
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
            if (Environment.SystemTarget == 64)
                _value = (nuint)value;
            else
                _value = checked((nuint)value);
        }

        public unsafe UIntPtr(void* value)
        {
            _value = (nuint)value;
        }

        public static int Size
        {
            get => Environment.SystemTarget / 8;
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => (obj is nuint other) && Equals(other);
        public override int GetHashCode()
        {
            if (Environment.Target64)
            {
                ulong value = _value;
                return value.GetHashCode();
            }
            else
            {
                return (int)_value;
            }
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
                //this = default;
                return; // returns default
            }
            //if (!typeof(T).IsValueType && array.GetType() != typeof(T[]))
            //    ThrowHelper.ThrowArrayTypeMismatchException();

            _reference = ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference<T>(array);
            _length = array.Length;
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
            if (Environment.Target64)
            {
                if ((ulong)(uint)start + (ulong)(uint)length > (ulong)(uint)_length)
                {
                    throw new ArgumentOutOfRangeException();
                }
            }
            else
            {
                if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
                {
                    throw new ArgumentOutOfRangeException();
                }
            }
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlySpan<T>(T[] array) => new ReadOnlySpan<T>(array);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlySpan<T>(Span<T> span) => new ReadOnlySpan<T>(ref span._reference, span.Length);

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
            if (Environment.Target64)
            {
                if ((ulong)(uint)start + (ulong)(uint)length > (ulong)(uint)_length)
                {
                    throw new ArgumentOutOfRangeException();
                }
            }
            else
            {
                if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
                {
                    throw new ArgumentOutOfRangeException();
                }
            }
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
            //Debug.Assert(dayNumber <= MaxDayNumber);
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

        internal static ReadOnlySpan<uint> DaysToMonth365 => [ 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365 ];
        internal static ReadOnlySpan<uint> DaysToMonth366 => [ 0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366 ];

        private static ReadOnlySpan<byte> DaysInMonth365 => [ 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 ];
        private static ReadOnlySpan<byte> DaysInMonth366 => [ 31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 ];

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
            //Debug.Assert((dateData & TicksMask) <= MaxTicks);
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
            //Debug.Assert(kind == DateTimeKind.Local, "Internal Constructor is for local times only");
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

            ///Debug.Assert(ticks <= MaxTicks, "Input parameters validated already");

            return ticks;
        }

        internal static ulong TimeToTicks(int hour, int minute, int second, int millisecond, int microsecond)
        {
            ulong ticks = TimeToTicks(hour, minute, second, millisecond);

            if ((uint)microsecond >= TimeSpan.MicrosecondsPerMillisecond) ThrowMicrosecondOutOfRange();

            ticks += (uint)microsecond * (uint)TimeSpan.TicksPerMicrosecond;

            //Debug.Assert(ticks <= MaxTicks, "Input parameters validated already");

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
    // interfaces
    public interface ITuple
    {
        int Length { get; }

        object this[int index] { get; }
    }
    public interface IDisposable
    {
        void Dispose();
    }
    public interface IFormatProvider
    {
        object GetFormat(Type formatType);
    }
    public interface IConvertible
    {
        TypeCode GetTypeCode();
    }
    public interface IFormatProvider
    {
        object? GetFormat(Type? formatType);
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Abs(float x)
        {
            return Math.Abs(x);
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
        public static void Write(sbyte value) { Write((int)value); }
        public static void Write(byte value) { Write((int)value); }
        public static void Write(short value) { Write((int)value); }
        public static void Write(ushort value) { Write((int)value); }
        public static unsafe void Write(int value)
        {
            char* buffer = stackalloc char[12];
            char* end = buffer + 11;
            char* p = end;
            *p = '\0';
            if (value == unchecked((int)0x80000000)) //int.MinValue
            {
                //-2147483648
                char* min = stackalloc char[] { '-', '2', '1', '4', '7', '4', '8', '3', '6', '4', '8', '\0' };
                _Write(min);
                return;
            }
            bool negative = value < 0;
            if (negative)
                value = -value;

            do
            {
                Int32 digit = value % 10;
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
            char* buffer = stackalloc char[21];
            char* end = buffer + 20;
            char* p = end;
            *p = '\0';

            if (value == unchecked((long)0x8000000000000000)) // long.MinValue
            {
                //-9223372036854775808
                char* min = stackalloc char[] { '-','9','2','2','3','3','7','2','0','3',
                                                '6','8','5','4','7','7','5','8','0','8','\0' };
                _Write(min);
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
        public static void Write(ulong value) { Write(value.ToString()); }
        public static void Write(float value) { Write((double)value); }
        public static unsafe void Write(double value)
        {
            _Write(value.ToString());
        }
        public static unsafe void Write(char value) { char* str = stackalloc char[] { value, '\0' }; _Write(str); }
        public static unsafe void Write(bool value)
        {
            if (value)
            {
                char* str = stackalloc char[] { 't', 'r', 'u', 'e', '\0' };
                _Write(str);
            }
            else
            {
                char* str = stackalloc char[] { 'f', 'a', 'l', 's', 'e', '\0' };
                _Write(str);
            }
        }
        public static unsafe void Write(char* value) { _Write(value); }
        public static void Write(ReadOnlySpan<char> value) { _Write(value); }
        public static void Write(string value) { _Write(value); }
        public static void Write(object value) { _Write(value.ToString()); }

        public static void WriteLine() { Write('\n'); }
        public static void WriteLine(sbyte value) { Write(value); Write('\n'); }
        public static void WriteLine(byte value) { Write(value); Write('\n'); }
        public static void WriteLine(short value) { Write(value); Write('\n'); }
        public static void WriteLine(ushort value) { Write(value); Write('\n'); }
        public static void WriteLine(int value) { Write(value); Write('\n'); }
        public static void WriteLine(uint value) { Write(value); Write('\n'); }
        public static void WriteLine(long value) { Write(value); Write('\n'); }
        public static void WriteLine(ulong value) { Write(value); Write('\n'); }
        public static void WriteLine(char value) { Write(value); Write('\n'); }
        public static void WriteLine(bool value) { Write(value); Write('\n'); }
        public static void WriteLine(float value) { Write(value); Write('\n'); }
        public static void WriteLine(double value) { Write(value); Write('\n'); }
        public static void WriteLine(string value) { Write(value); Write('\n'); }
        public static void WriteLine(ReadOnlySpan<char> value) { Write(value); Write('\n'); }
        public static unsafe void WriteLine(char* value) { Write(value); Write('\n'); }
        public static void WriteLine(object value)
        {
            if (value != null)
            {
                Write(value);
            }
            Write('\n');
        }
        // convenience aliases
        public static void print(bool value) { Write(value); }
        public static void print(char value) { Write(value); }
        public static void print(int value) { Write(value); }
        public static void print(ulong value) { Write(value); }
        public static void print(double value) { Write(value); }
        public static void print(string value) { WriteLine(value); }
        public static void print(object value) { WriteLine(value); }
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
namespace System.Runtime.InteropServices
{
    public static unsafe class MemoryMarshal
    {
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ref T GetArrayDataReference<T>(T[] array)
        {
            throw new NotSupportedException();
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
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
                //Debug.Assert(items is not null, ""Implementation depends on List<T> always having an array."");

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
}
namespace System.Runtime.CompilerServices
{
    public static class RuntimeHelpers
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static bool IsReferenceOrContainsReferences<T>() where T : allows ref struct => IsReferenceOrContainsReferences<T>();
        
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static bool IsKnownConstant(int value)
        {
            return false; // to do
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int GetHashCode(object o)
        {
            return 0; // to do
        }
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
        public static T ReadUnaligned<T>(scoped ref readonly byte source)
            where T : allows ref struct
        {
            return As<byte, T>(ref Unsafe.AsRef<byte>(in source));
            // ldarg.0
            // unaligned. 0x1
            // ldobj !!T
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
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
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static T As<T>(object o) where T : class?
        {
            throw new PlatformNotSupportedException();
            // ldarg.0
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ref TTo As<TFrom, TTo>(ref TFrom source)
            where TFrom : allows ref struct
            where TTo : allows ref struct
        {
            throw new PlatformNotSupportedException();
            // ldarg.0
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static unsafe ref T AsRef<T>(void* source)
            where T : allows ref struct
        {
            return ref *(T*)source;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ref T AsRef<T>(scoped ref readonly T source)
            where T : allows ref struct
        {
            //ldarg .0
            //ret
            return ref source;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ref T Add<T>(ref T source, int elementOffset)
            where T : allows ref struct
        {
            // ldarg .0
            // ldarg .1
            // sizeof !!T
            // conv.i
            // mul
            // add
            // ret
            return ref source;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static unsafe void* Add<T>(void* source, int elementOffset)
            where T : allows ref struct
        {
            // ldarg .0
            // ldarg .1
            // sizeof !!T
            // conv.i
            // mul
            // add
            // ret
            return (byte*)source + (elementOffset * (nint)sizeof(T));
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
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
        [MethodImpl(MethodImplOptions.NoInlining)]
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
        [MethodImpl(MethodImplOptions.NoInlining)]
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
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static nint ByteOffset<T>(ref T origin, ref T target)
            where T : allows ref struct
        {
            return 0;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int SizeOf<T>()
            where T : allows ref struct
        {
            return 0;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool AreSame<T>([AllowNull] ref readonly T left, [AllowNull] ref readonly T right)
            where T : allows ref struct
        {
            // ldarg.0
            // ldarg.1
            // ceq
            // ret
            return false;
        }
    }
}
namespace System.Runtime.Intrinsics
{
    public static class Vector128
    {
        internal const int Size = 16;

        internal const int Alignment = 16;
    }
}
namespace System.Reflection
{
    public interface IReflect
    {

    }
    public abstract class MemberInfo
    {

    }
}
namespace System.Globalization
{
    public enum UnicodeCategory
    {
        UppercaseLetter = 0,
        LowercaseLetter = 1,
        TitlecaseLetter = 2,
        ModifierLetter = 3,
        OtherLetter = 4,
        NonSpacingMark = 5,
        SpacingCombiningMark = 6,
        EnclosingMark = 7,
        DecimalDigitNumber = 8,
        LetterNumber = 9,
        OtherNumber = 10,
        SpaceSeparator = 11,
        LineSeparator = 12,
        ParagraphSeparator = 13,
        Control = 14,
        Format = 15,
        Surrogate = 16,
        PrivateUse = 17,
        ConnectorPunctuation = 18,
        DashPunctuation = 19,
        OpenPunctuation = 20,
        ClosePunctuation = 21,
        InitialQuotePunctuation = 22,
        FinalQuotePunctuation = 23,
        OtherPunctuation = 24,
        MathSymbol = 25,
        CurrencySymbol = 26,
        ModifierSymbol = 27,
        OtherSymbol = 28,
        OtherNotAssigned = 29,
    }
    [Flags]
    public enum NumberStyles
    {
        None = 0x00000000,

        /// <summary>
        /// Bit flag indicating that leading whitespace is allowed. Character values
        /// 0x0009, 0x000A, 0x000B, 0x000C, 0x000D, and 0x0020 are considered to be
        /// whitespace.
        /// </summary>
        AllowLeadingWhite = 0x00000001,

        /// <summary>
        /// Bitflag indicating trailing whitespace is allowed.
        /// </summary>
        AllowTrailingWhite = 0x00000002,

        /// <summary>
        /// Can the number start with a sign char specified by
        /// NumberFormatInfo.PositiveSign and NumberFormatInfo.NegativeSign
        /// </summary>
        AllowLeadingSign = 0x00000004,

        /// <summary>
        /// Allow the number to end with a sign char
        /// </summary>
        AllowTrailingSign = 0x00000008,

        /// <summary>
        /// Allow the number to be enclosed in parens
        /// </summary>
        AllowParentheses = 0x00000010,

        AllowDecimalPoint = 0x00000020,

        AllowThousands = 0x00000040,

        AllowExponent = 0x00000080,

        AllowCurrencySymbol = 0x00000100,

        AllowHexSpecifier = 0x00000200,

        AllowBinarySpecifier = 0x00000400,

        Integer = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign,

        HexNumber = AllowLeadingWhite | AllowTrailingWhite | AllowHexSpecifier,

        BinaryNumber = AllowLeadingWhite | AllowTrailingWhite | AllowBinarySpecifier,

        Number = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign | AllowTrailingSign |
                   AllowDecimalPoint | AllowThousands,

        Float = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign |
                   AllowDecimalPoint | AllowExponent,

        Currency = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign | AllowTrailingSign |
                   AllowParentheses | AllowDecimalPoint | AllowThousands | AllowCurrencySymbol,

        Any = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign | AllowTrailingSign |
                   AllowParentheses | AllowDecimalPoint | AllowThousands | AllowCurrencySymbol | AllowExponent,
    }
    internal class CultureData
    {
        private String sRealName;
        private bool bUseOverrides; // use user overrides?
        internal CultureData() { }
        internal CultureData(string name) { sRealName = string.Empty; }
        internal String CultureName
        {
            get
            {
                return sRealName;
            }
        }
        internal static CultureData GetCultureData(String cultureName, bool useUserOverride)
        {
            if (String.IsNullOrEmpty(cultureName))
            {
                return CultureData.Invariant;
            }
            CultureData culture = CreateCultureData(cultureName, useUserOverride);
            if (culture == null)
            {
                return null;
            }
            return culture;
        }
        private static CultureData CreateCultureData(string cultureName, bool useUserOverride)
        {
            CultureData culture = new CultureData();
            culture.bUseOverrides = useUserOverride;
            culture.sRealName = cultureName;

            return culture;
        }
        internal static readonly CultureData Invariant = new CultureData(string.Empty);
    }
    public class CultureInfo
    {
        private bool _isReadOnly;
        internal bool _isInherited;
        internal string _name;
        internal CultureData _cultureData;

        public CultureInfo(string name) : this(name, true)
        {
        }

        public CultureInfo(string name, bool useUserOverride)
        {
            if (name == null) throw new ArgumentNullException();

            CultureData cd = CultureData.GetCultureData(name, useUserOverride);
            if ((object)cd == null)
                throw new CultureNotFoundException(name, GetCultureNotSupportedExceptionMessage());
            _cultureData = cd;
            _name = _cultureData.CultureName;
            _isInherited = false;
        }

        private CultureInfo(CultureData cultureData, bool isReadOnly = false)
        {
            _cultureData = cultureData;
            _name = cultureData.CultureName;
            _isReadOnly = isReadOnly;
        }
        private static string GetCultureNotSupportedExceptionMessage() => "Culture not supported.";
        private static readonly CultureInfo s_InvariantCultureInfo = new CultureInfo(CultureData.Invariant, isReadOnly: true);
        public static CultureInfo InvariantCulture
        {
            get
            {
                return s_InvariantCultureInfo;
            }
        }
    }
    public enum CalendarAlgorithmType
    {
        Unknown = 0,            // This is the default value to return in the Calendar base class.
        SolarCalendar = 1,      // Solar-base calendar, such as GregorianCalendar, jaoaneseCalendar, JulianCalendar, etc.
                                // Solar calendars are based on the solar year and seasons.
        LunarCalendar = 2,      // Lunar-based calendar, such as Hijri and UmAlQuraCalendar.
                                // Lunar calendars are based on the path of the moon.  The seasons are not accurately represented.
        LunisolarCalendar = 3   // Lunisolar-based calendar which use leap month rule, such as HebrewCalendar and Asian Lunisolar calendars.
                                // Lunisolar calendars are based on the cycle of the moon, but consider the seasons as a secondary consideration,
                                // so they align with the seasons as well as lunar events.
    }
    public abstract class Calendar
    {
        internal const long MaxMillis = (long)DateTime.DaysTo10000 * TimeSpan.MillisecondsPerDay;

        private int _currentEraValue = -1;

        private bool _isReadOnly;

        public virtual DateTime MinSupportedDateTime => DateTime.MinValue;

        public virtual DateTime MaxSupportedDateTime => DateTime.MaxValue;

        public virtual CalendarAlgorithmType AlgorithmType => CalendarAlgorithmType.Unknown;

        protected Calendar()
        {
        }

        public const int CurrentEra = 0;

        public abstract bool IsLeapYear(int year, int era);

        public virtual DateTime ToDateTime(int year, int month, int day, int hour, int minute, int second, int millisecond)
        {
            return ToDateTime(year, month, day, hour, minute, second, millisecond, CurrentEra);
        }

        public abstract DateTime ToDateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, int era);
    }
}
namespace System.Text
{
    public sealed class StringBuilder
    {
        private char[] _buffer;
        private int _length;

        public StringBuilder() : this(16) { }

        public StringBuilder(int capacity)
        {
            if (capacity < 0) throw new System.ArgumentOutOfRangeException("capacity");
            _buffer = new char[capacity];
            _length = 0;
        }

        public int Length
        {
            get => _length;
            set
            {
                if (value < 0) throw new System.ArgumentOutOfRangeException("value");
                EnsureCapacity(value);
                if (value > _length)
                {
                    for (int i = _length; i < value; i++) _buffer[i] = '\0';
                }
                _length = value;
            }
        }

        public int Capacity => _buffer.Length;

        public StringBuilder Clear()
        {
            _length = 0;
            return this;
        }

        public override string ToString()
        {
            if (_length == 0) return System.String.Empty;
            return new string(_buffer, 0, _length);
        }

        public StringBuilder Append(char c)
        {
            EnsureCapacity(_length + 1);
            _buffer[_length++] = c;
            return this;
        }

        public StringBuilder Append(char c, int repeatCount)
        {
            if (repeatCount < 0) throw new System.ArgumentOutOfRangeException("repeatCount");
            EnsureCapacity(_length + repeatCount);
            for (int i = 0; i < repeatCount; i++)
                _buffer[_length++] = c;
            return this;
        }

        public StringBuilder Append(string s)
        {
            if ((object)s == null) return this;
            int n = s.Length;
            EnsureCapacity(_length + n);
            for (int i = 0; i < n; i++)
                _buffer[_length + i] = s[i];
            _length += n;
            return this;
        }

        public StringBuilder AppendLine()
            => Append(System.Environment.NewLine);

        private void EnsureCapacity(int desired)
        {
            if (desired <= _buffer.Length) return;

            int newCap = _buffer.Length == 0 ? 16 : _buffer.Length;
            while (newCap < desired)
                newCap = newCap * 2;

            var nb = new char[newCap];
            for (int i = 0; i < _length; i++)
                nb[i] = _buffer[i];
            _buffer = nb;
        }
    }
}
namespace System.Buffers
{
    public abstract class ArrayPool<T>
    {
        private static readonly SharedArrayPool<T> s_shared = new SharedArrayPool<T>();
        public static ArrayPool<T> Shared => s_shared;
        public static ArrayPool<T> Create() => new ConfigurableArrayPool<T>();

    }
    public class SharedArrayPool<T> : ArrayPool<T>
    {

    }
    public class ConfigurableArrayPool<T> : ArrayPool<T>
    {

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
namespace System.Drawing
{
    public enum KnownColor
    {
        // This enum is order dependent

        // 0 - reserved for "not a known color"

        // "System" colors, Part 1
        ActiveBorder = 1,
        ActiveCaption,
        ActiveCaptionText,
        AppWorkspace,
        Control,
        ControlDark,
        ControlDarkDark,
        ControlLight,
        ControlLightLight,
        ControlText,
        Desktop,
        GrayText,
        Highlight,
        HighlightText,
        HotTrack,
        InactiveBorder,
        InactiveCaption,
        InactiveCaptionText,
        Info,
        InfoText,
        Menu,
        MenuText,
        ScrollBar,
        Window,
        WindowFrame,
        WindowText,

        // "Web" Colors, Part 1
        Transparent,
        AliceBlue,
        AntiqueWhite,
        Aqua,
        Aquamarine,
        Azure,
        Beige,
        Bisque,
        Black,
        BlanchedAlmond,
        Blue,
        BlueViolet,
        Brown,
        BurlyWood,
        CadetBlue,
        Chartreuse,
        Chocolate,
        Coral,
        CornflowerBlue,
        Cornsilk,
        Crimson,
        Cyan,
        DarkBlue,
        DarkCyan,
        DarkGoldenrod,
        DarkGray,
        DarkGreen,
        DarkKhaki,
        DarkMagenta,
        DarkOliveGreen,
        DarkOrange,
        DarkOrchid,
        DarkRed,
        DarkSalmon,
        DarkSeaGreen,
        DarkSlateBlue,
        DarkSlateGray,
        DarkTurquoise,
        DarkViolet,
        DeepPink,
        DeepSkyBlue,
        DimGray,
        DodgerBlue,
        Firebrick,
        FloralWhite,
        ForestGreen,
        Fuchsia,
        Gainsboro,
        GhostWhite,
        Gold,
        Goldenrod,
        Gray,
        Green,
        GreenYellow,
        Honeydew,
        HotPink,
        IndianRed,
        Indigo,
        Ivory,
        Khaki,
        Lavender,
        LavenderBlush,
        LawnGreen,
        LemonChiffon,
        LightBlue,
        LightCoral,
        LightCyan,
        LightGoldenrodYellow,
        LightGray,
        LightGreen,
        LightPink,
        LightSalmon,
        LightSeaGreen,
        LightSkyBlue,
        LightSlateGray,
        LightSteelBlue,
        LightYellow,
        Lime,
        LimeGreen,
        Linen,
        Magenta,
        Maroon,
        MediumAquamarine,
        MediumBlue,
        MediumOrchid,
        MediumPurple,
        MediumSeaGreen,
        MediumSlateBlue,
        MediumSpringGreen,
        MediumTurquoise,
        MediumVioletRed,
        MidnightBlue,
        MintCream,
        MistyRose,
        Moccasin,
        NavajoWhite,
        Navy,
        OldLace,
        Olive,
        OliveDrab,
        Orange,
        OrangeRed,
        Orchid,
        PaleGoldenrod,
        PaleGreen,
        PaleTurquoise,
        PaleVioletRed,
        PapayaWhip,
        PeachPuff,
        Peru,
        Pink,
        Plum,
        PowderBlue,
        Purple,
        Red,
        RosyBrown,
        RoyalBlue,
        SaddleBrown,
        Salmon,
        SandyBrown,
        SeaGreen,
        SeaShell,
        Sienna,
        Silver,
        SkyBlue,
        SlateBlue,
        SlateGray,
        Snow,
        SpringGreen,
        SteelBlue,
        Tan,
        Teal,
        Thistle,
        Tomato,
        Turquoise,
        Violet,
        Wheat,
        White,
        WhiteSmoke,
        Yellow,
        YellowGreen,

        // "System" colors, Part 2
        ButtonFace,
        ButtonHighlight,
        ButtonShadow,
        GradientActiveCaption,
        GradientInactiveCaption,
        MenuBar,
        MenuHighlight,

        // "Web" colors, Part 2
        /// <summary>
        /// A system defined color representing the ARGB value <c>#663399</c>.
        /// </summary>
        RebeccaPurple,
    }
    public readonly struct Color : IEquatable<Color>
    {
        public static readonly Color Empty;

        private readonly string name;

        private readonly long value;

        private readonly short knownColor;

        private readonly short state;

        public static Color Transparent => new Color(KnownColor.Transparent);

        public static Color AliceBlue => new Color(KnownColor.AliceBlue);

        public static Color AntiqueWhite => new Color(KnownColor.AntiqueWhite);

        public static Color Aqua => new Color(KnownColor.Aqua);

        public static Color Aquamarine => new Color(KnownColor.Aquamarine);

        public static Color Azure => new Color(KnownColor.Azure);

        public static Color Beige => new Color(KnownColor.Beige);

        public static Color Bisque => new Color(KnownColor.Bisque);

        public static Color Black => new Color(KnownColor.Black);

        public static Color BlanchedAlmond => new Color(KnownColor.BlanchedAlmond);

        public static Color Blue => new Color(KnownColor.Blue);

        public static Color BlueViolet => new Color(KnownColor.BlueViolet);

        public static Color Brown => new Color(KnownColor.Brown);

        public static Color BurlyWood => new Color(KnownColor.BurlyWood);

        public static Color CadetBlue => new Color(KnownColor.CadetBlue);

        public static Color Chartreuse => new Color(KnownColor.Chartreuse);

        public static Color Chocolate => new Color(KnownColor.Chocolate);

        public static Color Coral => new Color(KnownColor.Coral);

        public static Color CornflowerBlue => new Color(KnownColor.CornflowerBlue);

        public static Color Cornsilk => new Color(KnownColor.Cornsilk);

        public static Color Crimson => new Color(KnownColor.Crimson);

        public static Color Cyan => new Color(KnownColor.Cyan);

        public static Color DarkBlue => new Color(KnownColor.DarkBlue);

        public static Color DarkCyan => new Color(KnownColor.DarkCyan);

        public static Color DarkGoldenrod => new Color(KnownColor.DarkGoldenrod);

        public static Color DarkGray => new Color(KnownColor.DarkGray);

        public static Color DarkGreen => new Color(KnownColor.DarkGreen);

        public static Color DarkKhaki => new Color(KnownColor.DarkKhaki);

        public static Color DarkMagenta => new Color(KnownColor.DarkMagenta);

        public static Color DarkOliveGreen => new Color(KnownColor.DarkOliveGreen);

        public static Color DarkOrange => new Color(KnownColor.DarkOrange);

        public static Color DarkOrchid => new Color(KnownColor.DarkOrchid);

        public static Color DarkRed => new Color(KnownColor.DarkRed);

        public static Color DarkSalmon => new Color(KnownColor.DarkSalmon);

        public static Color DarkSeaGreen => new Color(KnownColor.DarkSeaGreen);

        public static Color DarkSlateBlue => new Color(KnownColor.DarkSlateBlue);

        public static Color DarkSlateGray => new Color(KnownColor.DarkSlateGray);

        public static Color DarkTurquoise => new Color(KnownColor.DarkTurquoise);

        public static Color DarkViolet => new Color(KnownColor.DarkViolet);

        public static Color DeepPink => new Color(KnownColor.DeepPink);

        public static Color DeepSkyBlue => new Color(KnownColor.DeepSkyBlue);

        public static Color DimGray => new Color(KnownColor.DimGray);

        public static Color DodgerBlue => new Color(KnownColor.DodgerBlue);

        public static Color Firebrick => new Color(KnownColor.Firebrick);

        public static Color FloralWhite => new Color(KnownColor.FloralWhite);

        public static Color ForestGreen => new Color(KnownColor.ForestGreen);

        public static Color Fuchsia => new Color(KnownColor.Fuchsia);

        public static Color Gainsboro => new Color(KnownColor.Gainsboro);

        public static Color GhostWhite => new Color(KnownColor.GhostWhite);

        public static Color Gold => new Color(KnownColor.Gold);

        public static Color Goldenrod => new Color(KnownColor.Goldenrod);

        public static Color Gray => new Color(KnownColor.Gray);

        public static Color Green => new Color(KnownColor.Green);

        public static Color GreenYellow => new Color(KnownColor.GreenYellow);

        public static Color Honeydew => new Color(KnownColor.Honeydew);

        public static Color HotPink => new Color(KnownColor.HotPink);

        public static Color IndianRed => new Color(KnownColor.IndianRed);

        public static Color Indigo => new Color(KnownColor.Indigo);

        public static Color Ivory => new Color(KnownColor.Ivory);

        public static Color Khaki => new Color(KnownColor.Khaki);

        public static Color Lavender => new Color(KnownColor.Lavender);

        public static Color LavenderBlush => new Color(KnownColor.LavenderBlush);

        public static Color LawnGreen => new Color(KnownColor.LawnGreen);

        public static Color LemonChiffon => new Color(KnownColor.LemonChiffon);

        public static Color LightBlue => new Color(KnownColor.LightBlue);

        public static Color LightCoral => new Color(KnownColor.LightCoral);

        public static Color LightCyan => new Color(KnownColor.LightCyan);

        public static Color LightGoldenrodYellow => new Color(KnownColor.LightGoldenrodYellow);

        public static Color LightGreen => new Color(KnownColor.LightGreen);

        public static Color LightGray => new Color(KnownColor.LightGray);

        public static Color LightPink => new Color(KnownColor.LightPink);

        public static Color LightSalmon => new Color(KnownColor.LightSalmon);

        public static Color LightSeaGreen => new Color(KnownColor.LightSeaGreen);

        public static Color LightSkyBlue => new Color(KnownColor.LightSkyBlue);

        public static Color LightSlateGray => new Color(KnownColor.LightSlateGray);

        public static Color LightSteelBlue => new Color(KnownColor.LightSteelBlue);

        public static Color LightYellow => new Color(KnownColor.LightYellow);

        public static Color Lime => new Color(KnownColor.Lime);

        public static Color LimeGreen => new Color(KnownColor.LimeGreen);

        public static Color Linen => new Color(KnownColor.Linen);

        public static Color Magenta => new Color(KnownColor.Magenta);

        public static Color Maroon => new Color(KnownColor.Maroon);

        public static Color MediumAquamarine => new Color(KnownColor.MediumAquamarine);

        public static Color MediumBlue => new Color(KnownColor.MediumBlue);

        public static Color MediumOrchid => new Color(KnownColor.MediumOrchid);

        public static Color MediumPurple => new Color(KnownColor.MediumPurple);

        public static Color MediumSeaGreen => new Color(KnownColor.MediumSeaGreen);

        public static Color MediumSlateBlue => new Color(KnownColor.MediumSlateBlue);

        public static Color MediumSpringGreen => new Color(KnownColor.MediumSpringGreen);

        public static Color MediumTurquoise => new Color(KnownColor.MediumTurquoise);

        public static Color MediumVioletRed => new Color(KnownColor.MediumVioletRed);

        public static Color MidnightBlue => new Color(KnownColor.MidnightBlue);

        public static Color MintCream => new Color(KnownColor.MintCream);

        public static Color MistyRose => new Color(KnownColor.MistyRose);

        public static Color Moccasin => new Color(KnownColor.Moccasin);

        public static Color NavajoWhite => new Color(KnownColor.NavajoWhite);

        public static Color Navy => new Color(KnownColor.Navy);

        public static Color OldLace => new Color(KnownColor.OldLace);

        public static Color Olive => new Color(KnownColor.Olive);

        public static Color OliveDrab => new Color(KnownColor.OliveDrab);

        public static Color Orange => new Color(KnownColor.Orange);

        public static Color OrangeRed => new Color(KnownColor.OrangeRed);

        public static Color Orchid => new Color(KnownColor.Orchid);

        public static Color PaleGoldenrod => new Color(KnownColor.PaleGoldenrod);

        public static Color PaleGreen => new Color(KnownColor.PaleGreen);

        public static Color PaleTurquoise => new Color(KnownColor.PaleTurquoise);

        public static Color PaleVioletRed => new Color(KnownColor.PaleVioletRed);

        public static Color PapayaWhip => new Color(KnownColor.PapayaWhip);

        public static Color PeachPuff => new Color(KnownColor.PeachPuff);

        public static Color Peru => new Color(KnownColor.Peru);

        public static Color Pink => new Color(KnownColor.Pink);

        public static Color Plum => new Color(KnownColor.Plum);

        public static Color PowderBlue => new Color(KnownColor.PowderBlue);

        public static Color Purple => new Color(KnownColor.Purple);

        public static Color RebeccaPurple => new Color(KnownColor.RebeccaPurple);

        public static Color Red => new Color(KnownColor.Red);

        public static Color RosyBrown => new Color(KnownColor.RosyBrown);

        public static Color RoyalBlue => new Color(KnownColor.RoyalBlue);

        public static Color SaddleBrown => new Color(KnownColor.SaddleBrown);

        public static Color Salmon => new Color(KnownColor.Salmon);

        public static Color SandyBrown => new Color(KnownColor.SandyBrown);

        public static Color SeaGreen => new Color(KnownColor.SeaGreen);

        public static Color SeaShell => new Color(KnownColor.SeaShell);

        public static Color Sienna => new Color(KnownColor.Sienna);

        public static Color Silver => new Color(KnownColor.Silver);

        public static Color SkyBlue => new Color(KnownColor.SkyBlue);

        public static Color SlateBlue => new Color(KnownColor.SlateBlue);

        public static Color SlateGray => new Color(KnownColor.SlateGray);

        public static Color Snow => new Color(KnownColor.Snow);

        public static Color SpringGreen => new Color(KnownColor.SpringGreen);

        public static Color SteelBlue => new Color(KnownColor.SteelBlue);

        public static Color Tan => new Color(KnownColor.Tan);

        public static Color Teal => new Color(KnownColor.Teal);

        public static Color Thistle => new Color(KnownColor.Thistle);

        public static Color Tomato => new Color(KnownColor.Tomato);

        public static Color Turquoise => new Color(KnownColor.Turquoise);

        public static Color Violet => new Color(KnownColor.Violet);

        public static Color Wheat => new Color(KnownColor.Wheat);

        public static Color White => new Color(KnownColor.White);

        public static Color WhiteSmoke => new Color(KnownColor.WhiteSmoke);

        public static Color Yellow => new Color(KnownColor.Yellow);

        public static Color YellowGreen => new Color(KnownColor.YellowGreen);

        public byte R => (byte)(Value >> 16);

        public byte G => (byte)(Value >> 8);

        public byte B => (byte)Value;

        public byte A => (byte)(Value >> 24);

        public bool IsKnownColor => (state & 1) != 0;

        public bool IsEmpty => state == 0;

        public bool IsNamedColor
        {
            get
            {
                if ((state & 8) == 0)
                {
                    return IsKnownColor;
                }

                return true;
            }
        }

        public bool IsSystemColor
        {
            get
            {
                if (IsKnownColor)
                {
                    return IsKnownColorSystem((KnownColor)knownColor);
                }

                return false;
            }
        }

        private string NameAndARGBValue => $"{{Name = {Name}, ARGB = ({A}, {R}, {G}, {B})}}";

        public string Name
        {
            get
            {
                if ((state & 8) != 0)
                {
                    return name;
                }

                if (IsKnownColor)
                {
                    return KnownColorNames.KnownColorToName((KnownColor)knownColor);
                }

                return value.ToString("x");
            }
        }

        private long Value
        {
            get
            {
                if ((state & 2) != 0)
                {
                    return value;
                }

                if (IsKnownColor)
                {
                    return KnownColorTable.KnownColorToArgb((KnownColor)knownColor);
                }

                return 0L;
            }
        }

        internal Color(KnownColor knownColor)
        {
            value = 0L;
            state = 1;
            name = null;
            this.knownColor = (short)knownColor;
        }

        private Color(long value, short state, string name, KnownColor knownColor)
        {
            this.value = value;
            this.state = state;
            this.name = name;
            this.knownColor = (short)knownColor;
        }

        internal static bool IsKnownColorSystem(KnownColor knownColor)
        {
            return KnownColorTable.ColorKindTable[(int)knownColor] == 0;
        }

        private static void CheckByte(int value, string name)
        {
            if ((uint)value > 255u)
            {
                ThrowOutOfByteRange(value, name);
            }

            static void ThrowOutOfByteRange(int v, string n)
            {
                throw new ArgumentException();
            }
        }

        private static Color FromArgb(uint argb)
        {
            return new Color(argb, 2, null, (KnownColor)0);
        }

        public static Color FromArgb(int argb)
        {
            return FromArgb((uint)argb);
        }

        public static Color FromArgb(int alpha, int red, int green, int blue)
        {
            CheckByte(alpha, "alpha");
            CheckByte(red, "red");
            CheckByte(green, "green");
            CheckByte(blue, "blue");
            return FromArgb((uint)((alpha << 24) | (red << 16) | (green << 8) | blue));
        }

        public static Color FromArgb(int alpha, Color baseColor)
        {
            CheckByte(alpha, "alpha");
            return FromArgb((uint)((alpha << 24) | ((int)baseColor.Value & 0xFFFFFF)));
        }

        public static Color FromArgb(int red, int green, int blue)
        {
            return FromArgb(255, red, green, blue);
        }

        public static Color FromKnownColor(KnownColor color)
        {
            if (color > (KnownColor)0 && color <= KnownColor.RebeccaPurple)
            {
                return new Color(color);
            }

            return FromName(color.ToString());
        }

        public static Color FromName(string name)
        {
            if (ColorTable.TryGetNamedColor(name, out var result))
            {
                return result;
            }

            return new Color(0L, 8, name, (KnownColor)0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void GetRgbValues(out int r, out int g, out int b)
        {
            uint num = (uint)Value;
            r = (int)(num & 0xFF0000) >> 16;
            g = (int)(num & 0xFF00) >> 8;
            b = (int)(num & 0xFF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MinMaxRgb(out int min, out int max, int r, int g, int b)
        {
            if (r > g)
            {
                max = r;
                min = g;
            }
            else
            {
                max = g;
                min = r;
            }

            if (b > max)
            {
                max = b;
            }
            else if (b < min)
            {
                min = b;
            }
        }

        public float GetBrightness()
        {
            GetRgbValues(out var r, out var g, out var b);
            MinMaxRgb(out var min, out var max, r, g, b);
            return (float)(max + min) / 510f;
        }

        public float GetHue()
        {
            GetRgbValues(out var r, out var g, out var b);
            if (r == g && g == b)
            {
                return 0f;
            }

            MinMaxRgb(out var min, out var max, r, g, b);
            float num = max - min;
            float num2 = ((r == max) ? ((float)(g - b) / num) : ((g != max) ? ((float)(r - g) / num + 4f) : ((float)(b - r) / num + 2f)));
            num2 *= 60f;
            if (num2 < 0f)
            {
                num2 += 360f;
            }

            return num2;
        }

        public float GetSaturation()
        {
            GetRgbValues(out var r, out var g, out var b);
            if (r == g && g == b)
            {
                return 0f;
            }

            MinMaxRgb(out var min, out var max, r, g, b);
            int num = max + min;
            if (num > 255)
            {
                num = 510 - max - min;
            }

            return (float)(max - min) / (float)num;
        }

        public int ToArgb()
        {
            return (int)Value;
        }

        public KnownColor ToKnownColor()
        {
            return (KnownColor)knownColor;
        }

        public override string ToString()
        {
            if (!IsNamedColor)
            {
                if ((state & 2) == 0)
                {
                    return "Color [Empty]";
                }

                return $"{"Color"} [A={A}, R={R}, G={G}, B={B}]";
            }

            return "Color [" + Name + "]";
        }

        public static bool operator ==(Color left, Color right)
        {
            if (left.value == right.value && left.state == right.state && left.knownColor == right.knownColor)
            {
                return left.name == right.name;
            }

            return false;
        }

        public static bool operator !=(Color left, Color right)
        {
            return !(left == right);
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is Color other)
            {
                return Equals(other);
            }

            return false;
        }

        public bool Equals(Color other)
        {
            return this == other;
        }

        public override int GetHashCode()
        {
            if (name != null && !IsKnownColor)
            {
                return name.GetHashCode();
            }

            return value.GetHashCode();
        }
    }
    internal static class ColorTable
    {
        public static bool TryGetNamedColor(string name, out Color result)
        {
            result = default(Color);
            return false;
        }
        internal static bool IsKnownNamedColor(string name)
        {
            return false;
        }
    }
    internal static class KnownColorNames
    {
        private static readonly string[] s_colorNameTable = new string[]
       {
            // "System" colors, Part 1
            "ActiveBorder",
            "ActiveCaption",
            "ActiveCaptionText",
            "AppWorkspace",
            "Control",
            "ControlDark",
            "ControlDarkDark",
            "ControlLight",
            "ControlLightLight",
            "ControlText",
            "Desktop",
            "GrayText",
            "Highlight",
            "HighlightText",
            "HotTrack",
            "InactiveBorder",
            "InactiveCaption",
            "InactiveCaptionText",
            "Info",
            "InfoText",
            "Menu",
            "MenuText",
            "ScrollBar",
            "Window",
            "WindowFrame",
            "WindowText",

            // "Web" Colors, Part 1
            "Transparent",
            "AliceBlue",
            "AntiqueWhite",
            "Aqua",
            "Aquamarine",
            "Azure",
            "Beige",
            "Bisque",
            "Black",
            "BlanchedAlmond",
            "Blue",
            "BlueViolet",
            "Brown",
            "BurlyWood",
            "CadetBlue",
            "Chartreuse",
            "Chocolate",
            "Coral",
            "CornflowerBlue",
            "Cornsilk",
            "Crimson",
            "Cyan",
            "DarkBlue",
            "DarkCyan",
            "DarkGoldenrod",
            "DarkGray",
            "DarkGreen",
            "DarkKhaki",
            "DarkMagenta",
            "DarkOliveGreen",
            "DarkOrange",
            "DarkOrchid",
            "DarkRed",
            "DarkSalmon",
            "DarkSeaGreen",
            "DarkSlateBlue",
            "DarkSlateGray",
            "DarkTurquoise",
            "DarkViolet",
            "DeepPink",
            "DeepSkyBlue",
            "DimGray",
            "DodgerBlue",
            "Firebrick",
            "FloralWhite",
            "ForestGreen",
            "Fuchsia",
            "Gainsboro",
            "GhostWhite",
            "Gold",
            "Goldenrod",
            "Gray",
            "Green",
            "GreenYellow",
            "Honeydew",
            "HotPink",
            "IndianRed",
            "Indigo",
            "Ivory",
            "Khaki",
            "Lavender",
            "LavenderBlush",
            "LawnGreen",
            "LemonChiffon",
            "LightBlue",
            "LightCoral",
            "LightCyan",
            "LightGoldenrodYellow",
            "LightGray",
            "LightGreen",
            "LightPink",
            "LightSalmon",
            "LightSeaGreen",
            "LightSkyBlue",
            "LightSlateGray",
            "LightSteelBlue",
            "LightYellow",
            "Lime",
            "LimeGreen",
            "Linen",
            "Magenta",
            "Maroon",
            "MediumAquamarine",
            "MediumBlue",
            "MediumOrchid",
            "MediumPurple",
            "MediumSeaGreen",
            "MediumSlateBlue",
            "MediumSpringGreen",
            "MediumTurquoise",
            "MediumVioletRed",
            "MidnightBlue",
            "MintCream",
            "MistyRose",
            "Moccasin",
            "NavajoWhite",
            "Navy",
            "OldLace",
            "Olive",
            "OliveDrab",
            "Orange",
            "OrangeRed",
            "Orchid",
            "PaleGoldenrod",
            "PaleGreen",
            "PaleTurquoise",
            "PaleVioletRed",
            "PapayaWhip",
            "PeachPuff",
            "Peru",
            "Pink",
            "Plum",
            "PowderBlue",
            "Purple",
            "Red",
            "RosyBrown",
            "RoyalBlue",
            "SaddleBrown",
            "Salmon",
            "SandyBrown",
            "SeaGreen",
            "SeaShell",
            "Sienna",
            "Silver",
            "SkyBlue",
            "SlateBlue",
            "SlateGray",
            "Snow",
            "SpringGreen",
            "SteelBlue",
            "Tan",
            "Teal",
            "Thistle",
            "Tomato",
            "Turquoise",
            "Violet",
            "Wheat",
            "White",
            "WhiteSmoke",
            "Yellow",
            "YellowGreen",

            // "System" colors, Part 2
            "ButtonFace",
            "ButtonHighlight",
            "ButtonShadow",
            "GradientActiveCaption",
            "GradientInactiveCaption",
            "MenuBar",
            "MenuHighlight",

            // "Web" colors, Part 2
            "RebeccaPurple",
       };

        public static string KnownColorToName(KnownColor color)
        {
            return s_colorNameTable[unchecked((int)color) - 1];
        }
    }
    internal static class KnownColorTable
    {
        public const byte KnownColorKindSystem = 0;
        public const byte KnownColorKindWeb = 1;
        public const byte KnownColorKindUnknown = 2;

        public static ReadOnlySpan<uint> ColorValueTable =>
        [
             // "not a known color"
            0,
            // "System" colors, Part 1
            0xFFD4D0C8,     // ActiveBorder
            0xFF0054E3,     // ActiveCaption
            0xFFFFFFFF,     // ActiveCaptionText
            0xFF808080,     // AppWorkspace
            0xFFECE9D8,     // Control
            0xFFACA899,     // ControlDark
            0xFF716F64,     // ControlDarkDark
            0xFFF1EFE2,     // ControlLight
            0xFFFFFFFF,     // ControlLightLight
            0xFF000000,     // ControlText
            0xFF004E98,     // Desktop
            0xFFACA899,     // GrayText
            0xFF316AC5,     // Highlight
            0xFFFFFFFF,     // HighlightText
            0xFF000080,     // HotTrack
            0xFFD4D0C8,     // InactiveBorder
            0xFF7A96DF,     // InactiveCaption
            0xFFD8E4F8,     // InactiveCaptionText
            0xFFFFFFE1,     // Info
            0xFF000000,     // InfoText
            0xFFFFFFFF,     // Menu
            0xFF000000,     // MenuText
            0xFFD4D0C8,     // ScrollBar
            0xFFFFFFFF,     // Window
            0xFF000000,     // WindowFrame
            0xFF000000,     // WindowText

            // "Web" Colors, Part 1
            0x00FFFFFF,     // Transparent
            0xFFF0F8FF,     // AliceBlue
            0xFFFAEBD7,     // AntiqueWhite
            0xFF00FFFF,     // Aqua
            0xFF7FFFD4,     // Aquamarine
            0xFFF0FFFF,     // Azure
            0xFFF5F5DC,     // Beige
            0xFFFFE4C4,     // Bisque
            0xFF000000,     // Black
            0xFFFFEBCD,     // BlanchedAlmond
            0xFF0000FF,     // Blue
            0xFF8A2BE2,     // BlueViolet
            0xFFA52A2A,     // Brown
            0xFFDEB887,     // BurlyWood
            0xFF5F9EA0,     // CadetBlue
            0xFF7FFF00,     // Chartreuse
            0xFFD2691E,     // Chocolate
            0xFFFF7F50,     // Coral
            0xFF6495ED,     // CornflowerBlue
            0xFFFFF8DC,     // Cornsilk
            0xFFDC143C,     // Crimson
            0xFF00FFFF,     // Cyan
            0xFF00008B,     // DarkBlue
            0xFF008B8B,     // DarkCyan
            0xFFB8860B,     // DarkGoldenrod
            0xFFA9A9A9,     // DarkGray
            0xFF006400,     // DarkGreen
            0xFFBDB76B,     // DarkKhaki
            0xFF8B008B,     // DarkMagenta
            0xFF556B2F,     // DarkOliveGreen
            0xFFFF8C00,     // DarkOrange
            0xFF9932CC,     // DarkOrchid
            0xFF8B0000,     // DarkRed
            0xFFE9967A,     // DarkSalmon
            0xFF8FBC8F,     // DarkSeaGreen
            0xFF483D8B,     // DarkSlateBlue
            0xFF2F4F4F,     // DarkSlateGray
            0xFF00CED1,     // DarkTurquoise
            0xFF9400D3,     // DarkViolet
            0xFFFF1493,     // DeepPink
            0xFF00BFFF,     // DeepSkyBlue
            0xFF696969,     // DimGray
            0xFF1E90FF,     // DodgerBlue
            0xFFB22222,     // Firebrick
            0xFFFFFAF0,     // FloralWhite
            0xFF228B22,     // ForestGreen
            0xFFFF00FF,     // Fuchsia
            0xFFDCDCDC,     // Gainsboro
            0xFFF8F8FF,     // GhostWhite
            0xFFFFD700,     // Gold
            0xFFDAA520,     // Goldenrod
            0xFF808080,     // Gray
            0xFF008000,     // Green
            0xFFADFF2F,     // GreenYellow
            0xFFF0FFF0,     // Honeydew
            0xFFFF69B4,     // HotPink
            0xFFCD5C5C,     // IndianRed
            0xFF4B0082,     // Indigo
            0xFFFFFFF0,     // Ivory
            0xFFF0E68C,     // Khaki
            0xFFE6E6FA,     // Lavender
            0xFFFFF0F5,     // LavenderBlush
            0xFF7CFC00,     // LawnGreen
            0xFFFFFACD,     // LemonChiffon
            0xFFADD8E6,     // LightBlue
            0xFFF08080,     // LightCoral
            0xFFE0FFFF,     // LightCyan
            0xFFFAFAD2,     // LightGoldenrodYellow
            0xFFD3D3D3,     // LightGray
            0xFF90EE90,     // LightGreen
            0xFFFFB6C1,     // LightPink
            0xFFFFA07A,     // LightSalmon
            0xFF20B2AA,     // LightSeaGreen
            0xFF87CEFA,     // LightSkyBlue
            0xFF778899,     // LightSlateGray
            0xFFB0C4DE,     // LightSteelBlue
            0xFFFFFFE0,     // LightYellow
            0xFF00FF00,     // Lime
            0xFF32CD32,     // LimeGreen
            0xFFFAF0E6,     // Linen
            0xFFFF00FF,     // Magenta
            0xFF800000,     // Maroon
            0xFF66CDAA,     // MediumAquamarine
            0xFF0000CD,     // MediumBlue
            0xFFBA55D3,     // MediumOrchid
            0xFF9370DB,     // MediumPurple
            0xFF3CB371,     // MediumSeaGreen
            0xFF7B68EE,     // MediumSlateBlue
            0xFF00FA9A,     // MediumSpringGreen
            0xFF48D1CC,     // MediumTurquoise
            0xFFC71585,     // MediumVioletRed
            0xFF191970,     // MidnightBlue
            0xFFF5FFFA,     // MintCream
            0xFFFFE4E1,     // MistyRose
            0xFFFFE4B5,     // Moccasin
            0xFFFFDEAD,     // NavajoWhite
            0xFF000080,     // Navy
            0xFFFDF5E6,     // OldLace
            0xFF808000,     // Olive
            0xFF6B8E23,     // OliveDrab
            0xFFFFA500,     // Orange
            0xFFFF4500,     // OrangeRed
            0xFFDA70D6,     // Orchid
            0xFFEEE8AA,     // PaleGoldenrod
            0xFF98FB98,     // PaleGreen
            0xFFAFEEEE,     // PaleTurquoise
            0xFFDB7093,     // PaleVioletRed
            0xFFFFEFD5,     // PapayaWhip
            0xFFFFDAB9,     // PeachPuff
            0xFFCD853F,     // Peru
            0xFFFFC0CB,     // Pink
            0xFFDDA0DD,     // Plum
            0xFFB0E0E6,     // PowderBlue
            0xFF800080,     // Purple
            0xFFFF0000,     // Red
            0xFFBC8F8F,     // RosyBrown
            0xFF4169E1,     // RoyalBlue
            0xFF8B4513,     // SaddleBrown
            0xFFFA8072,     // Salmon
            0xFFF4A460,     // SandyBrown
            0xFF2E8B57,     // SeaGreen
            0xFFFFF5EE,     // SeaShell
            0xFFA0522D,     // Sienna
            0xFFC0C0C0,     // Silver
            0xFF87CEEB,     // SkyBlue
            0xFF6A5ACD,     // SlateBlue
            0xFF708090,     // SlateGray
            0xFFFFFAFA,     // Snow
            0xFF00FF7F,     // SpringGreen
            0xFF4682B4,     // SteelBlue
            0xFFD2B48C,     // Tan
            0xFF008080,     // Teal
            0xFFD8BFD8,     // Thistle
            0xFFFF6347,     // Tomato
            0xFF40E0D0,     // Turquoise
            0xFFEE82EE,     // Violet
            0xFFF5DEB3,     // Wheat
            0xFFFFFFFF,     // White
            0xFFF5F5F5,     // WhiteSmoke
            0xFFFFFF00,     // Yellow
            0xFF9ACD32,     // YellowGreen

            // "System" colors, Part 2
            0xFFF0F0F0,     // ButtonFace
            0xFFFFFFFF,     // ButtonHighlight
            0xFFA0A0A0,     // ButtonShadow
            0xFFB9D1EA,     // GradientActiveCaption
            0xFFD7E4F2,     // GradientInactiveCaption
            0xFFF0F0F0,     // MenuBar
            0xFF3399FF,     // MenuHighlight

            // "Web" colors, Part 2
            0xFF663399,     // RebeccaPurple
        ];

        public static ReadOnlySpan<byte> ColorKindTable =>
        [
            // "not a known color"
            KnownColorKindUnknown,

            // "System" colors, Part 1
            KnownColorKindSystem,       // ActiveBorder
            KnownColorKindSystem,       // ActiveCaption
            KnownColorKindSystem,       // ActiveCaptionText
            KnownColorKindSystem,       // AppWorkspace
            KnownColorKindSystem,       // Control
            KnownColorKindSystem,       // ControlDark
            KnownColorKindSystem,       // ControlDarkDark
            KnownColorKindSystem,       // ControlLight
            KnownColorKindSystem,       // ControlLightLight
            KnownColorKindSystem,       // ControlText
            KnownColorKindSystem,       // Desktop
            KnownColorKindSystem,       // GrayText
            KnownColorKindSystem,       // Highlight
            KnownColorKindSystem,       // HighlightText
            KnownColorKindSystem,       // HotTrack
            KnownColorKindSystem,       // InactiveBorder
            KnownColorKindSystem,       // InactiveCaption
            KnownColorKindSystem,       // InactiveCaptionText
            KnownColorKindSystem,       // Info
            KnownColorKindSystem,       // InfoText
            KnownColorKindSystem,       // Menu
            KnownColorKindSystem,       // MenuText
            KnownColorKindSystem,       // ScrollBar
            KnownColorKindSystem,       // Window
            KnownColorKindSystem,       // WindowFrame
            KnownColorKindSystem,       // WindowText

            // "Web" Colors, Part 1
            KnownColorKindWeb,      // Transparent
            KnownColorKindWeb,      // AliceBlue
            KnownColorKindWeb,      // AntiqueWhite
            KnownColorKindWeb,      // Aqua
            KnownColorKindWeb,      // Aquamarine
            KnownColorKindWeb,      // Azure
            KnownColorKindWeb,      // Beige
            KnownColorKindWeb,      // Bisque
            KnownColorKindWeb,      // Black
            KnownColorKindWeb,      // BlanchedAlmond
            KnownColorKindWeb,      // Blue
            KnownColorKindWeb,      // BlueViolet
            KnownColorKindWeb,      // Brown
            KnownColorKindWeb,      // BurlyWood
            KnownColorKindWeb,      // CadetBlue
            KnownColorKindWeb,      // Chartreuse
            KnownColorKindWeb,      // Chocolate
            KnownColorKindWeb,      // Coral
            KnownColorKindWeb,      // CornflowerBlue
            KnownColorKindWeb,      // Cornsilk
            KnownColorKindWeb,      // Crimson
            KnownColorKindWeb,      // Cyan
            KnownColorKindWeb,      // DarkBlue
            KnownColorKindWeb,      // DarkCyan
            KnownColorKindWeb,      // DarkGoldenrod
            KnownColorKindWeb,      // DarkGray
            KnownColorKindWeb,      // DarkGreen
            KnownColorKindWeb,      // DarkKhaki
            KnownColorKindWeb,      // DarkMagenta
            KnownColorKindWeb,      // DarkOliveGreen
            KnownColorKindWeb,      // DarkOrange
            KnownColorKindWeb,      // DarkOrchid
            KnownColorKindWeb,      // DarkRed
            KnownColorKindWeb,      // DarkSalmon
            KnownColorKindWeb,      // DarkSeaGreen
            KnownColorKindWeb,      // DarkSlateBlue
            KnownColorKindWeb,      // DarkSlateGray
            KnownColorKindWeb,      // DarkTurquoise
            KnownColorKindWeb,      // DarkViolet
            KnownColorKindWeb,      // DeepPink
            KnownColorKindWeb,      // DeepSkyBlue
            KnownColorKindWeb,      // DimGray
            KnownColorKindWeb,      // DodgerBlue
            KnownColorKindWeb,      // Firebrick
            KnownColorKindWeb,      // FloralWhite
            KnownColorKindWeb,      // ForestGreen
            KnownColorKindWeb,      // Fuchsia
            KnownColorKindWeb,      // Gainsboro
            KnownColorKindWeb,      // GhostWhite
            KnownColorKindWeb,      // Gold
            KnownColorKindWeb,      // Goldenrod
            KnownColorKindWeb,      // Gray
            KnownColorKindWeb,      // Green
            KnownColorKindWeb,      // GreenYellow
            KnownColorKindWeb,      // Honeydew
            KnownColorKindWeb,      // HotPink
            KnownColorKindWeb,      // IndianRed
            KnownColorKindWeb,      // Indigo
            KnownColorKindWeb,      // Ivory
            KnownColorKindWeb,      // Khaki
            KnownColorKindWeb,      // Lavender
            KnownColorKindWeb,      // LavenderBlush
            KnownColorKindWeb,      // LawnGreen
            KnownColorKindWeb,      // LemonChiffon
            KnownColorKindWeb,      // LightBlue
            KnownColorKindWeb,      // LightCoral
            KnownColorKindWeb,      // LightCyan
            KnownColorKindWeb,      // LightGoldenrodYellow
            KnownColorKindWeb,      // LightGray
            KnownColorKindWeb,      // LightGreen
            KnownColorKindWeb,      // LightPink
            KnownColorKindWeb,      // LightSalmon
            KnownColorKindWeb,      // LightSeaGreen
            KnownColorKindWeb,      // LightSkyBlue
            KnownColorKindWeb,      // LightSlateGray
            KnownColorKindWeb,      // LightSteelBlue
            KnownColorKindWeb,      // LightYellow
            KnownColorKindWeb,      // Lime
            KnownColorKindWeb,      // LimeGreen
            KnownColorKindWeb,      // Linen
            KnownColorKindWeb,      // Magenta
            KnownColorKindWeb,      // Maroon
            KnownColorKindWeb,      // MediumAquamarine
            KnownColorKindWeb,      // MediumBlue
            KnownColorKindWeb,      // MediumOrchid
            KnownColorKindWeb,      // MediumPurple
            KnownColorKindWeb,      // MediumSeaGreen
            KnownColorKindWeb,      // MediumSlateBlue
            KnownColorKindWeb,      // MediumSpringGreen
            KnownColorKindWeb,      // MediumTurquoise
            KnownColorKindWeb,      // MediumVioletRed
            KnownColorKindWeb,      // MidnightBlue
            KnownColorKindWeb,      // MintCream
            KnownColorKindWeb,      // MistyRose
            KnownColorKindWeb,      // Moccasin
            KnownColorKindWeb,      // NavajoWhite
            KnownColorKindWeb,      // Navy
            KnownColorKindWeb,      // OldLace
            KnownColorKindWeb,      // Olive
            KnownColorKindWeb,      // OliveDrab
            KnownColorKindWeb,      // Orange
            KnownColorKindWeb,      // OrangeRed
            KnownColorKindWeb,      // Orchid
            KnownColorKindWeb,      // PaleGoldenrod
            KnownColorKindWeb,      // PaleGreen
            KnownColorKindWeb,      // PaleTurquoise
            KnownColorKindWeb,      // PaleVioletRed
            KnownColorKindWeb,      // PapayaWhip
            KnownColorKindWeb,      // PeachPuff
            KnownColorKindWeb,      // Peru
            KnownColorKindWeb,      // Pink
            KnownColorKindWeb,      // Plum
            KnownColorKindWeb,      // PowderBlue
            KnownColorKindWeb,      // Purple
            KnownColorKindWeb,      // Red
            KnownColorKindWeb,      // RosyBrown
            KnownColorKindWeb,      // RoyalBlue
            KnownColorKindWeb,      // SaddleBrown
            KnownColorKindWeb,      // Salmon
            KnownColorKindWeb,      // SandyBrown
            KnownColorKindWeb,      // SeaGreen
            KnownColorKindWeb,      // SeaShell
            KnownColorKindWeb,      // Sienna
            KnownColorKindWeb,      // Silver
            KnownColorKindWeb,      // SkyBlue
            KnownColorKindWeb,      // SlateBlue
            KnownColorKindWeb,      // SlateGray
            KnownColorKindWeb,      // Snow
            KnownColorKindWeb,      // SpringGreen
            KnownColorKindWeb,      // SteelBlue
            KnownColorKindWeb,      // Tan
            KnownColorKindWeb,      // Teal
            KnownColorKindWeb,      // Thistle
            KnownColorKindWeb,      // Tomato
            KnownColorKindWeb,      // Turquoise
            KnownColorKindWeb,      // Violet
            KnownColorKindWeb,      // Wheat
            KnownColorKindWeb,      // White
            KnownColorKindWeb,      // WhiteSmoke
            KnownColorKindWeb,      // Yellow
            KnownColorKindWeb,      // YellowGreen

            // "System" colors, Part 1
            KnownColorKindSystem,       // ButtonFace
            KnownColorKindSystem,       // ButtonHighlight
            KnownColorKindSystem,       // ButtonShadow
            KnownColorKindSystem,       // GradientActiveCaption
            KnownColorKindSystem,       // GradientInactiveCaption
            KnownColorKindSystem,       // MenuBar
            KnownColorKindSystem,       // MenuHighlight

            // "Web" colors, Part 2
            KnownColorKindWeb,      // RebeccaPurple
        ];

        private static ReadOnlySpan<uint> AlternateSystemColors =>
        [
            0,          // To align with KnownColor.ActiveBorder = 1

                        // Existing   New
            0xFF464646, // FFB4B4B4 - FF464646: ActiveBorder - Dark gray
            0xFF3C5F78, // FF99B4D1 - FF3C5F78: ActiveCaption - Highlighted Text Background
            0xFFFFFFFF, // FF000000 - FFBEBEBE: ActiveCaptionText - White
            0xFF3C3C3C, // FFABABAB - FF3C3C3C: AppWorkspace - Panel Background
            0xFF202020, // FFF0F0F0 - FF373737: Control - Normal Panel/Windows Background
            0xFF4A4A4A, // FFA0A0A0 - FF464646: ControlDark - A lighter gray for dark mode
            0xFF5A5A5A, // FF696969 - FF5A5A5A: ControlDarkDark - An even lighter gray for dark mode
            0xFF2E2E2E, // FFE3E3E3 - FF2E2E2E: ControlLight - Unfocused Textbox Background
            0xFF1F1F1F, // FFFFFFFF - FF1F1F1F: ControlLightLight - Focused Textbox Background
            0xFFFFFFFF, // FF000000 - FFFFFFFF: ControlText - Control Forecolor and Text Color
            0xFF101010, // FF000000 - FF101010: Desktop - Black
            0xFF969696, // FF6D6D6D - FF969696: GrayText - Prompt Text Focused TextBox
            0xFF2864B4, // FF0078D7 - FF2864B4: Highlight - Highlighted Panel in DarkMode
            0xFF000000, // FFFFFFFF - FF000000: HighlightText - White
            0xFF2D5FAF, // FF0066CC - FF2D5FAF: HotTrack - Background of the ToggleSwitch
            0xFF3C3F41, // FFF4F7FC - FF3C3F41: InactiveBorder - Dark gray
            0xFF374B5A, // FFBFCBDD - FF374B5A: InactiveCaption - Highlighted Panel in DarkMode
            0xFFBEBEBE, // FF000000 - FFBEBEBE: InactiveCaptionText - Middle Dark Panel
            0xFF50503C, // FFFFFFE1 - FF50503C: Info - Link Label
            0xFFBEBEBE, // FF000000 - FFBEBEBE: InfoText - Prompt Text Color
            0xFF373737, // FFF0F0F0 - FF373737: Menu - Normal Menu Background
            0xFFF0F0F0, // FF000000 - FFF0F0F0: MenuText - White
            0xFF505050, // FFC8C8C8 - FF505050: ScrollBar - Scrollbars and Scrollbar Arrows
            0xFF323232, // FFFFFFFF - FF323232: Window - Window Background
            0xFF282828, // FF646464 - FF282828: WindowFrame - White
            0xFFF0F0F0, // FF000000 - FFF0F0F0: WindowText - White
            0xFF202020, // FFF0F0F0 - FF373737: ButtonFace - Same as Window Background
            0xFF101010, // FFFFFFFF - FF101010: ButtonHighlight - White
            0xFF464646, // FFA0A0A0 - FF464646: ButtonShadow - Same as Scrollbar Elements
            0XFF416482, // FFB9D1EA - FF416482: GradientActiveCaption - Same as Highlighted Text Background
            0xFF557396, // FFD7E4F2 - FF557396: GradientInactiveCaption - Same as Highlighted Panel in DarkMode
            0xFF373737, // FFF0F0F0 - FF373737: MenuBar - Same as Normal Menu Background
            0xFF2A80D2  // FF3399FF - FF2A80D2: MenuHighlight - Same as Highlighted Menu Background
        ];

        internal static Color ArgbToKnownColor(uint argb)
        {
            ReadOnlySpan<uint> colorValueTable = ColorValueTable;
            for (int index = 1; index < colorValueTable.Length; ++index)
            {
                if (ColorKindTable[index] == KnownColorKindWeb && colorValueTable[index] == argb)
                {
                    return Color.FromKnownColor((KnownColor)index);
                }
            }

            // Not a known color
            return Color.FromArgb((int)argb);
        }

        public static uint KnownColorToArgb(KnownColor color)
        {
            return ColorKindTable[(int)color] == KnownColorKindSystem
                 ? GetSystemColorArgb(color)
                 : ColorValueTable[(int)color];
        }

        private static uint GetAlternateSystemColorArgb(KnownColor color)
        {
            // Shift the original (split) index to fit the alternate color map.
            int index = color <= KnownColor.WindowText
                ? (int)color
                : (int)color - (int)KnownColor.ButtonFace + (int)KnownColor.WindowText + 1;

            return AlternateSystemColors[index];
        }

        public static uint GetSystemColorArgb(KnownColor color)
        {
            return ColorValueTable[(int)color];
        }
    }
    /// <summary>
    /// Represents the size of a rectangular region with an ordered pair of width and height.
    /// </summary>
    public struct Size
    {
        public static readonly Size Empty;

        private int width; // Do not rename
        private int height; // Do not rename

        public Size(Point pt)
        {
            width = pt.X;
            height = pt.Y;
        }

        public Size(int width, int height)
        {
            this.width = width;
            this.height = height;
        }

        public static Size operator +(Size sz1, Size sz2) => Add(sz1, sz2);

        public static Size operator -(Size sz1, Size sz2) => Subtract(sz1, sz2);

        public static explicit operator Point(Size size) => new Point(size.Width, size.Height);

        public readonly bool IsEmpty => width == 0 && height == 0;

        public int Width
        {
            readonly get => width;
            set => width = value;
        }

        public int Height
        {
            readonly get => height;
            set => height = value;
        }

        public static Size Add(Size sz1, Size sz2) =>
            new Size(unchecked(sz1.Width + sz2.Width), unchecked(sz1.Height + sz2.Height));

        public static Size Subtract(Size sz1, Size sz2) =>
            new Size(unchecked(sz1.Width - sz2.Width), unchecked(sz1.Height - sz2.Height));

        /// <summary>
        /// Converts a SizeF to a Size by performing a truncate operation on all the coordinates.
        /// </summary>
        public static Size Truncate(SizeF value) => new Size(unchecked((int)value.Width), unchecked((int)value.Height));

        /// <summary>
        /// Converts a SizeF to a Size by performing a round operation on all the coordinates.
        /// </summary>
        public static Size Round(SizeF value) =>
            new Size(unchecked((int)Math.Round(value.Width)), unchecked((int)Math.Round(value.Height)));

        public override readonly string ToString() => $"{{Width={width}, Height={height}}}";

        private static Size Multiply(Size size, int multiplier) =>
            new Size(unchecked(size.width * multiplier), unchecked(size.height * multiplier));

        private static SizeF Multiply(Size size, float multiplier) =>
            new SizeF(size.width * multiplier, size.height * multiplier);
    }
    /// <summary>
    /// Represents an ordered pair of x and y coordinates that define a point in a two-dimensional plane.
    /// </summary>
    public struct Point : IEquatable<Point>
    {
        public static readonly Point Empty;

        private int x; // Do not rename
        private int y; // Do not rename

        public Point(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
        public Point(Size sz)
        {
            x = sz.Width;
            y = sz.Height;
        }
        /// <summary>
        /// Initializes a new instance of the Point class using coordinates specified by an integer value.
        /// </summary>
        public Point(int dw)
        {
            x = LowInt16(dw);
            y = HighInt16(dw);
        }

        public readonly bool IsEmpty => x == 0 && y == 0;

        public int X
        {
            readonly get => x;
            set => x = value;
        }

        public int Y
        {
            readonly get => y;
            set => y = value;
        }

        public static explicit operator Size(Point p) => new Size(p.X, p.Y);

        public static Point operator +(Point pt, Size sz) => Add(pt, sz);

        public static Point operator -(Point pt, Size sz) => Subtract(pt, sz);

        public static bool operator ==(Point left, Point right) => left.X == right.X && left.Y == right.Y;

        public static bool operator !=(Point left, Point right) => !(left == right);

        public static Point Add(Point pt, Size sz) => new Point(unchecked(pt.X + sz.Width), unchecked(pt.Y + sz.Height));

        public static Point Subtract(Point pt, Size sz) => new Point(unchecked(pt.X - sz.Width), unchecked(pt.Y - sz.Height));

        public static Point Truncate(PointF value) => new Point(unchecked((int)value.X), unchecked((int)value.Y));

        public static Point Round(PointF value) => new Point(unchecked((int)Math.Round(value.X)), unchecked((int)Math.Round(value.Y)));

        public void Offset(int dx, int dy)
        {
            unchecked
            {
                X += dx;
                Y += dy;
            }
        }

        public void Offset(Point p) => Offset(p.X, p.Y);

        public override readonly string ToString() => $"{{X={X},Y={Y}}}";

        private static short HighInt16(int n) => unchecked((short)((n >> 16) & 0xffff));

        private static short LowInt16(int n) => unchecked((short)(n & 0xffff));
    }
    /// <summary>
    /// Represents the size of a rectangular region with an ordered pair of width and height.
    /// </summary>
    public struct SizeF
    {
        public static readonly SizeF Empty;
        private float width; // Do not rename
        private float height; // Do not rename

        public SizeF(SizeF size)
        {
            width = size.width;
            height = size.height;
        }

        public SizeF(PointF pt)
        {
            width = pt.X;
            height = pt.Y;
        }
        public SizeF(System.Numerics.Vector2 vector)
        {
            width = vector.X;
            height = vector.Y;
        }
        public System.Numerics.Vector2 ToVector2() => new System.Numerics.Vector2(width, height);
        public SizeF(float width, float height)
        {
            this.width = width;
            this.height = height;
        }

        public static explicit operator System.Numerics.Vector2(SizeF size) => size.ToVector2();

        public static explicit operator SizeF(System.Numerics.Vector2 vector) => new SizeF(vector);



        public readonly bool IsEmpty => width == 0 && height == 0;

        public float Width
        {
            readonly get => width;
            set => width = value;
        }

        public float Height
        {
            readonly get => height;
            set => height = value;
        }
    }
    /// <summary>
    /// Represents an ordered pair of x and y coordinates that define a point in a two-dimensional plane.
    /// </summary>
    public struct PointF
    {
        public static readonly PointF Empty;
        private float x; // Do not rename
        private float y; // Do not rename

        public PointF(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public PointF(System.Numerics.Vector2 vector)
        {
            x = vector.X;
            y = vector.Y;
        }

        public System.Numerics.Vector2 ToVector2() => new System.Numerics.Vector2(x, y);
        public readonly bool IsEmpty => x == 0f && y == 0f;

        public float X
        {
            readonly get => x;
            set => x = value;
        }
        public float Y
        {
            readonly get => y;
            set => y = value;
        }

        public static explicit operator System.Numerics.Vector2(PointF point) => point.ToVector2();
        public static explicit operator PointF(System.Numerics.Vector2 vector) => new PointF(vector);

    }
}
namespace System.Numerics
{
    public static class BitOperations
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint RotateLeft(uint value, int offset)
            => (value << offset) | (value >> (32 - offset));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RotateLeft(ulong value, int offset)
            => (value << offset) | (value >> (64 - offset));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint RotateRight(uint value, int offset)
            => (value >> offset) | (value << (32 - offset));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RotateRight(ulong value, int offset)
            => (value >> offset) | (value << (64 - offset));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(int value) => (value & (value - 1)) == 0 && value > 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(uint value) => (value & (value - 1)) == 0 && value != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(long value) => (value & (value - 1)) == 0 && value > 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(ulong value) => (value & (value - 1)) == 0 && value != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(nint value) => (value & (value - 1)) == 0 && value > 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPow2(nuint value) => (value & (value - 1)) == 0 && value != 0;
    }

    public readonly struct BigInteger
    {
        internal const uint kuMaskHighBit = unchecked((uint)int.MinValue);
        internal const int kcbitUint = 32;
        internal const int kcbitUlong = 64;
        internal const int DecimalScaleFactorMask = 0x00FF0000;

        internal static int MaxLength => Array.MaxLength / kcbitUint;

        internal readonly int _sign; // Do not rename
        internal readonly uint[]? _bits; // Do not rename

        private static readonly BigInteger s_bnMinInt = new BigInteger(-1, new uint[] { kuMaskHighBit });
        private static readonly BigInteger s_bnOneInt = new BigInteger(1);
        private static readonly BigInteger s_bnZeroInt = new BigInteger(0);
        private static readonly BigInteger s_bnMinusOneInt = new BigInteger(-1);

        public BigInteger(int value)
        {
            if (value == int.MinValue)
                this = s_bnMinInt;
            else
            {
                _sign = value;
                _bits = null;
            }

        }

        public BigInteger(uint value)
        {
            if (value <= int.MaxValue)
            {
                _sign = (int)value;
                _bits = null;
            }
            else
            {
                _sign = +1;
                _bits = new uint[1];
                _bits[0] = value;
            }

        }

        public BigInteger(long value)
        {
            if (int.MinValue < value && value <= int.MaxValue)
            {
                _sign = (int)value;
                _bits = null;
            }
            else if (value == int.MinValue)
            {
                this = s_bnMinInt;
            }
            else
            {
                ulong x;
                if (value < 0)
                {
                    x = unchecked((ulong)-value);
                    _sign = -1;
                }
                else
                {
                    x = (ulong)value;
                    _sign = +1;
                }

                if (x <= uint.MaxValue)
                {
                    _bits = new uint[1];
                    _bits[0] = (uint)x;
                }
                else
                {
                    _bits = new uint[2];
                    _bits[0] = unchecked((uint)x);
                    _bits[1] = (uint)(x >> kcbitUint);
                }
            }

        }

        public BigInteger(ulong value)
        {
            if (value <= int.MaxValue)
            {
                _sign = (int)value;
                _bits = null;
            }
            else if (value <= uint.MaxValue)
            {
                _sign = +1;
                _bits = new uint[1];
                _bits[0] = (uint)value;
            }
            else
            {
                _sign = +1;
                _bits = new uint[2];
                _bits[0] = unchecked((uint)value);
                _bits[1] = (uint)(value >> kcbitUint);
            }

        }

        internal BigInteger(int n, uint[]? rgu)
        {
            if ((rgu is not null) && (rgu.Length > MaxLength))
            {
                throw new OverflowException();
            }

            _sign = n;
            _bits = rgu;

        }

        public static BigInteger Zero { get { return s_bnZeroInt; } }

        public static BigInteger One { get { return s_bnOneInt; } }

        public static BigInteger MinusOne { get { return s_bnMinusOneInt; } }


        public bool IsZero { get { return _sign == 0; } }

        public bool IsOne { get { return _sign == 1 && _bits == null; } }

        public bool IsEven { get { return _bits == null ? (_sign & 1) == 0 : (_bits[0] & 1) == 0; } }

        public int Sign
        {
            get { return (_sign >> (kcbitUint - 1)) - (-_sign >> (kcbitUint - 1)); }
        }

        public static int Compare(BigInteger left, BigInteger right)
        {
            return left.CompareTo(right);
        }

        public static BigInteger Abs(BigInteger value)
        {
            return (value >= Zero) ? value : -value;
        }

        public static BigInteger Add(BigInteger left, BigInteger right)
        {
            return left + right;
        }

        public static BigInteger Subtract(BigInteger left, BigInteger right)
        {
            return left - right;
        }

        public static (BigInteger Quotient, BigInteger Remainder) DivRem(BigInteger left, BigInteger right)
        {
            BigInteger quotient = DivRem(left, right, out BigInteger remainder);
            return (quotient, remainder);
        }
        public static BigInteger DivRem(BigInteger dividend, BigInteger divisor, out BigInteger remainder)
        {
            if (divisor.IsZero)
                throw new DivideByZeroException();

            if (dividend.IsZero)
            {
                remainder = Zero;
                return Zero;
            }

            bool quotientNegative = (dividend._sign < 0) ^ (divisor._sign < 0);
            bool remainderNegative = dividend._sign < 0;

            uint[] dividendMagnitude = GetMagnitudeArray(dividend);
            uint[] divisorMagnitude = GetMagnitudeArray(divisor);

            uint[] quotientMagnitude = BigIntegerCalculator.Divide(
                dividendMagnitude,
                divisorMagnitude,
                out uint[] remainderMagnitude);

            remainder = CreateFromMagnitude(remainderMagnitude, remainderNegative);
            return CreateFromMagnitude(quotientMagnitude, quotientNegative);
        }

        public static BigInteger Negate(BigInteger value)
        {
            return -value;
        }

        public static BigInteger Max(BigInteger left, BigInteger right)
        {
            if (left.CompareTo(right) < 0)
                return right;
            return left;
        }

        public static BigInteger Min(BigInteger left, BigInteger right)
        {
            if (left.CompareTo(right) <= 0)
                return left;
            return right;
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is BigInteger other && Equals(other);
        }
        public bool Equals(BigInteger other)
        {
            if (_sign != other._sign)
                return false;

            if (_bits == other._bits)
                return true;

            if (_bits == null || other._bits == null)
                return false;

            int length = BigIntegerCalculator.GetLength(_bits);
            if (length != BigIntegerCalculator.GetLength(other._bits))
                return false;

            for (int i = 0; i < length; i++)
            {
                if (_bits[i] != other._bits[i])
                    return false;
            }

            return true;
        }

        public override int GetHashCode()
        {
            int hash = _sign;

            if (_bits != null)
            {
                int length = BigIntegerCalculator.GetLength(_bits);
                for (int i = 0; i < length; i++)
                    hash = unchecked(hash * 31 + (int)_bits[i]);
            }

            return hash;
        }

        public int CompareTo(long other)
        {
            if (_bits == null)
                return ((long)_sign).CompareTo(other);
            int cu;
            if ((_sign ^ other) < 0 || (cu = _bits.Length) > 2)
                return _sign;
            ulong uu = other < 0 ? (ulong)-other : (ulong)other;
            ulong uuTmp = cu == 2 ? NumericsHelpers.MakeUInt64(_bits[1], _bits[0]) : _bits[0];
            return _sign * uuTmp.CompareTo(uu);
        }
        public int CompareTo(ulong other)
        {
            if (_sign < 0)
                return -1;
            if (_bits == null)
                return ((ulong)_sign).CompareTo(other);
            int cu = _bits.Length;
            if (cu > 2)
                return +1;
            ulong uuTmp = cu == 2 ? NumericsHelpers.MakeUInt64(_bits[1], _bits[0]) : _bits[0];
            return uuTmp.CompareTo(other);
        }
        public int CompareTo(BigInteger other)
        {
            if ((_sign ^ other._sign) < 0)
            {
                // Different signs, so the comparison is easy.
                return _sign < 0 ? -1 : +1;
            }

            // Same signs
            if (_bits == null)
            {
                if (other._bits == null)
                    return _sign < other._sign ? -1 : _sign > other._sign ? +1 : 0;
                return -other._sign;
            }

            if (other._bits == null)
                return _sign;

            int bitsResult = BigIntegerCalculator.Compare(_bits, other._bits);
            return _sign < 0 ? -bitsResult : bitsResult;
        }

        

        public int CompareTo(object? obj)
        {
            if (obj == null)
                return 1;
            if (obj is not BigInteger bigInt)
                throw new ArgumentException();
            return CompareTo(bigInt);
        }

        public static BigInteger operator <<(BigInteger value, int shift)
        {
            if (shift == 0 || value.IsZero)
                return value;

            if (shift == int.MinValue)
                return (value >> int.MaxValue) >> 1;

            if (shift < 0)
                return value >> -shift;

            uint[] magnitude = GetMagnitudeArray(value);
            uint[] shifted = BigIntegerCalculator.ShiftLeft(magnitude, shift);

            return CreateFromMagnitude(shifted, value._sign < 0);
        }

        public static BigInteger operator >>(BigInteger value, int shift)
        {
            if (shift == 0 || value.IsZero)
                return value;

            if (shift == int.MinValue)
                return (value << int.MaxValue) << 1;

            if (shift < 0)
                return value << -shift;

            uint[] magnitude = GetMagnitudeArray(value);

            if (value._sign >= 0)
            {
                uint[] shifted = BigIntegerCalculator.ShiftRight(magnitude, shift);
                return CreateFromMagnitude(shifted, false);
            }
            else
            {
                // -m >> shift == -ceil(m / 2^shift)
                uint[] shifted = BigIntegerCalculator.ShiftRight(magnitude, shift);

                if (BigIntegerCalculator.HasNonZeroLowerBits(magnitude, shift))
                    shifted = BigIntegerCalculator.Add(shifted, 1u);

                return CreateFromMagnitude(shifted, true);
            }
        }

        private static uint[] GetMagnitudeArray(BigInteger value)
        {
            if (value._bits != null)
                return value._bits;

            if (value._sign == 0)
                return Array.Empty<uint>();

            return new uint[] { AbsAsUInt(value._sign) };
        }

        public static BigInteger operator ~(BigInteger value)
        {
            return -(value + One);
        }

        public static BigInteger operator -(BigInteger value)
        {
            return new BigInteger(-value._sign, value._bits);
        }

        public static BigInteger operator +(BigInteger value)
        {
            return value;
        }

        public static BigInteger operator ++(BigInteger value)
        {
            return value + One;
        }

        public static BigInteger operator --(BigInteger value)
        {
            return value - One;
        }

        public static BigInteger operator +(BigInteger left, BigInteger right)
        {
            if (left._bits == null && right._bits == null)
                return new BigInteger((long)left._sign + (long)right._sign);

            if (left._sign < 0 != right._sign < 0)
                return Subtract(left._bits, left._sign, right._bits, -1 * right._sign);
            return Add(left._bits, left._sign, right._bits, right._sign);
        }

        public static BigInteger operator -(BigInteger left, BigInteger right)
        {
            if (left._bits == null && right._bits == null)
                return new BigInteger((long)left._sign - (long)right._sign);

            if (left._sign < 0 != right._sign < 0)
                return Add(left._bits, left._sign, right._bits, -1 * right._sign);
            return Subtract(left._bits, left._sign, right._bits, right._sign);
        }

        public static BigInteger operator *(BigInteger left, BigInteger right)
        {
            if (left._bits == null && right._bits == null)
                return (long)left._sign * right._sign;

            return Multiply(left._bits, left._sign, right._bits, right._sign);
        }

        public static BigInteger operator /(BigInteger dividend, BigInteger divisor)
        {
            if (divisor.IsZero)
                throw new DivideByZeroException();

            if (dividend.IsZero)
                return Zero;

            bool negative = (dividend._sign < 0) ^ (divisor._sign < 0);

            uint[] quotient = BigIntegerCalculator.Divide(
                GetMagnitudeArray(dividend),
                GetMagnitudeArray(divisor));

            return CreateFromMagnitude(quotient, negative);
        }

        public static BigInteger operator %(BigInteger dividend, BigInteger divisor)
        {
            if (divisor.IsZero)
                throw new DivideByZeroException();

            if (dividend.IsZero)
                return Zero;

            uint[] remainder = BigIntegerCalculator.Remainder(
                GetMagnitudeArray(dividend),
                GetMagnitudeArray(divisor));

            return CreateFromMagnitude(remainder, dividend._sign < 0);
        }

        private static BigInteger Subtract(ReadOnlySpan<uint> leftBits, int leftSign, ReadOnlySpan<uint> rightBits, int rightSign)
        {
            bool trivialLeft = leftBits.IsEmpty;
            bool trivialRight = rightBits.IsEmpty;

            if (trivialLeft && trivialRight)
            {
                return new BigInteger((long)leftSign - (long)rightSign);
            }

            uint[] resultBits;

            if (trivialLeft)
            {
                resultBits = BigIntegerCalculator.Subtract(rightBits, AbsAsUInt(leftSign));
                return CreateFromMagnitude(resultBits, leftSign >= 0);
            }

            if (trivialRight)
            {
                resultBits = BigIntegerCalculator.Subtract(leftBits, AbsAsUInt(rightSign));
                return CreateFromMagnitude(resultBits, leftSign < 0);
            }

            int cmp = BigIntegerCalculator.Compare(leftBits, rightBits);

            if (cmp < 0)
            {
                resultBits = BigIntegerCalculator.Subtract(rightBits, leftBits);
                return CreateFromMagnitude(resultBits, leftSign >= 0);
            }

            resultBits = BigIntegerCalculator.Subtract(leftBits, rightBits);
            return CreateFromMagnitude(resultBits, leftSign < 0);
        }

        private static BigInteger Add(ReadOnlySpan<uint> leftBits, int leftSign, ReadOnlySpan<uint> rightBits, int rightSign)
        {
            bool trivialLeft = leftBits.IsEmpty;
            bool trivialRight = rightBits.IsEmpty;

            if (trivialLeft && trivialRight)
            {
                return new BigInteger((long)leftSign + (long)rightSign);
            }

            uint[] resultBits;

            if (trivialLeft)
            {
                resultBits = BigIntegerCalculator.Add(rightBits, AbsAsUInt(leftSign));
                return CreateFromMagnitude(resultBits, leftSign < 0);
            }

            if (trivialRight)
            {
                resultBits = BigIntegerCalculator.Add(leftBits, AbsAsUInt(rightSign));
                return CreateFromMagnitude(resultBits, leftSign < 0);
            }

            resultBits = BigIntegerCalculator.Add(leftBits, rightBits);
            return CreateFromMagnitude(resultBits, leftSign < 0);
        }

        private static BigInteger Multiply(ReadOnlySpan<uint> left, int leftSign, ReadOnlySpan<uint> right, int rightSign)
        {
            if (leftSign == 0 || rightSign == 0)
                return Zero;

            bool negative = (leftSign < 0) ^ (rightSign < 0);

            if (left.IsEmpty)
            {
                uint small = AbsAsUInt(leftSign);
                uint[] bits = BigIntegerCalculator.Multiply(right, small);
                return CreateFromMagnitude(bits, negative);
            }

            if (right.IsEmpty)
            {
                uint small = AbsAsUInt(rightSign);
                uint[] bits = BigIntegerCalculator.Multiply(left, small);
                return CreateFromMagnitude(bits, negative);
            }

            uint[] result = BigIntegerCalculator.Multiply(left, right);
            return CreateFromMagnitude(result, negative);
        }

        private static uint AbsAsUInt(int value)
        {
            if (value >= 0)
                return (uint)value;

            return (uint)(-((long)value));
        }
        private static BigInteger CreateFromMagnitude(uint[] bits, bool negative)
        {
            int length = BigIntegerCalculator.GetLength(bits);

            if (length == 0)
                return Zero;

            if (length == 1)
            {
                uint value = bits[0];

                if (!negative && value <= int.MaxValue)
                    return new BigInteger((int)value);

                if (negative && value <= 0x80000000u)
                {
                    if (value == 0x80000000u)
                        return new BigInteger(int.MinValue);

                    return new BigInteger(-(int)value);
                }
            }

            uint[] normalized = new uint[length];

            for (int i = 0; i < length; i++)
                normalized[i] = bits[i];

            return new BigInteger(negative ? -1 : 1, normalized);
        }

        public static bool operator <(BigInteger left, BigInteger right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(BigInteger left, BigInteger right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >(BigInteger left, BigInteger right)
        {
            return left.CompareTo(right) > 0;
        }
        public static bool operator >=(BigInteger left, BigInteger right)
        {
            return left.CompareTo(right) >= 0;
        }

        public static bool operator ==(BigInteger left, BigInteger right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BigInteger left, BigInteger right)
        {
            return !left.Equals(right);
        }

        public static bool operator <(BigInteger left, long right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(BigInteger left, long right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >(BigInteger left, long right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator >=(BigInteger left, long right)
        {
            return left.CompareTo(right) >= 0;
        }

        public static bool operator ==(BigInteger left, long right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BigInteger left, long right)
        {
            return !left.Equals(right);
        }

        public static bool operator <(long left, BigInteger right)
        {
            return right.CompareTo(left) > 0;
        }

        public static bool operator <=(long left, BigInteger right)
        {
            return right.CompareTo(left) >= 0;
        }

        public static bool operator >(long left, BigInteger right)
        {
            return right.CompareTo(left) < 0;
        }

        public static bool operator >=(long left, BigInteger right)
        {
            return right.CompareTo(left) <= 0;
        }

        public static bool operator ==(long left, BigInteger right)
        {
            return right.Equals(left);
        }

        public static bool operator !=(long left, BigInteger right)
        {
            return !right.Equals(left);
        }

        public static bool operator <(BigInteger left, ulong right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(BigInteger left, ulong right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >(BigInteger left, ulong right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator >=(BigInteger left, ulong right)
        {
            return left.CompareTo(right) >= 0;
        }

        public static bool operator ==(BigInteger left, ulong right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BigInteger left, ulong right)
        {
            return !left.Equals(right);
        }

        public static bool operator <(ulong left, BigInteger right)
        {
            return right.CompareTo(left) > 0;
        }

        public static bool operator <=(ulong left, BigInteger right)
        {
            return right.CompareTo(left) >= 0;
        }

        public static bool operator >(ulong left, BigInteger right)
        {
            return right.CompareTo(left) < 0;
        }

        public static bool operator >=(ulong left, BigInteger right)
        {
            return right.CompareTo(left) <= 0;
        }

        public static bool operator ==(ulong left, BigInteger right)
        {
            return right.Equals(left);
        }

        public static bool operator !=(ulong left, BigInteger right)
        {
            return !right.Equals(left);
        }


        public static implicit operator BigInteger(int value)
        {
            return new BigInteger(value);
        }

        public static implicit operator BigInteger(long value)
        {
            return new BigInteger(value);
        }

        public static explicit operator byte(BigInteger value)
        {
            return checked((byte)((int)value));
        }

        public static explicit operator char(BigInteger value)
        {
            return checked((char)((int)value));
        }

        public static explicit operator short(BigInteger value)
        {
            return checked((short)((int)value));
        }

        public static explicit operator int(BigInteger value)
        {
            if (value._bits == null)
            {
                return value._sign;  // Value packed into int32 sign
            }
            if (value._bits.Length > 1)
            {
                // More than 32 bits
                throw new OverflowException();
            }
            if (value._sign > 0)
            {
                return checked((int)value._bits[0]);
            }
            if (value._bits[0] > kuMaskHighBit)
            {
                // Value > Int32.MinValue
                throw new OverflowException();
            }
            return unchecked(-(int)value._bits[0]);
        }

        public static explicit operator long(BigInteger value)
        {
            if (value._bits == null)
            {
                return value._sign;
            }

            int len = value._bits.Length;
            if (len > 2)
            {
                throw new OverflowException();
            }

            ulong uu;
            if (len > 1)
            {
                uu = NumericsHelpers.MakeUInt64(value._bits[1], value._bits[0]);
            }
            else
            {
                uu = value._bits[0];
            }

            long ll = value._sign > 0 ? unchecked((long)uu) : unchecked(-(long)uu);
            if ((ll > 0 && value._sign > 0) || (ll < 0 && value._sign < 0))
            {
                // Signs match, no overflow
                return ll;
            }
            throw new OverflowException();
        }
    }
    internal static class BigIntegerCalculator
    {

        internal static int GetLength(uint[] bits)
        {
            int length = bits.Length;

            while (length > 0 && bits[length - 1] == 0)
                length--;

            return length;
        }
        private static int GetLength(ReadOnlySpan<uint> bits)
        {
            int length = bits.Length;

            while (length > 0 && bits[length - 1] == 0)
                length--;

            return length;
        }
        internal static int Compare(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
        {
            int leftLength = GetLength(left);
            int rightLength = GetLength(right);

            if (leftLength < rightLength)
                return -1;

            if (leftLength > rightLength)
                return 1;

            for (int i = leftLength - 1; i >= 0; i--)
            {
                uint l = left[i];
                uint r = right[i];

                if (l < r)
                    return -1;

                if (l > r)
                    return 1;
            }

            return 0;

        }
        internal static uint[] ShiftLeft(ReadOnlySpan<uint> value, int shift)
        {
            int length = GetLength(value);
            if (length == 0)
                return Array.Empty<uint>();

            int digitShift = shift / 32;
            int smallShift = shift & 31;

            long resultLengthLong = (long)length + digitShift + 1;
            if (resultLengthLong > BigInteger.MaxLength)
                throw new OverflowException();

            int resultLength = (int)resultLengthLong;
            uint[] result = new uint[resultLength];

            if (smallShift == 0)
            {
                for (int i = 0; i < length; i++)
                    result[i + digitShift] = value[i];
            }
            else
            {
                int carryShift = 32 - smallShift;
                uint carry = 0;

                for (int i = 0; i < length; i++)
                {
                    uint current = value[i];
                    result[i + digitShift] = (current << smallShift) | carry;
                    carry = current >> carryShift;
                }

                result[length + digitShift] = carry;
            }

            return result;
        }
        internal static uint[] ShiftRight(ReadOnlySpan<uint> value, int shift)
        {
            int length = GetLength(value);
            if (length == 0)
                return Array.Empty<uint>();

            int digitShift = shift / 32;
            int smallShift = shift & 31;

            if (digitShift >= length)
                return Array.Empty<uint>();

            int resultLength = length - digitShift;
            uint[] result = new uint[resultLength];

            if (smallShift == 0)
            {
                for (int i = 0; i < resultLength; i++)
                    result[i] = value[i + digitShift];
            }
            else
            {
                int carryShift = 32 - smallShift;
                uint carry = 0;

                for (int i = length - 1; i >= digitShift; i--)
                {
                    uint current = value[i];
                    result[i - digitShift] = (current >> smallShift) | carry;
                    carry = current << carryShift;
                }
            }

            return result;
        }

        internal static bool HasNonZeroLowerBits(ReadOnlySpan<uint> value, int bitCount)
        {
            int length = GetLength(value);
            if (length == 0 || bitCount <= 0)
                return false;

            int fullWords = bitCount / 32;
            int partialBits = bitCount & 31;

            int wordsToCheck = fullWords < length ? fullWords : length;

            for (int i = 0; i < wordsToCheck; i++)
            {
                if (value[i] != 0)
                    return true;
            }

            if (partialBits != 0 && fullWords < length)
            {
                uint mask = (1u << partialBits) - 1u;
                if ((value[fullWords] & mask) != 0)
                    return true;
            }

            return false;
        }

        internal static uint[] Add(ReadOnlySpan<uint> left, uint right)
        {
            uint[] result = new uint[left.Length + 1];

            ulong carry = right;
            int i = 0;

            for (; i < left.Length; i++)
            {
                ulong sum = (ulong)left[i] + carry;
                result[i] = (uint)sum;
                carry = sum >> 32;

                if (carry == 0)
                {
                    i++;
                    break;
                }
            }

            for (; i < left.Length; i++)
                result[i] = left[i];

            result[left.Length] = (uint)carry;
            return result;
        }
        internal static uint[] Add(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
        {
            if (left.Length < right.Length)
            {
                ReadOnlySpan<uint> tmp = left;
                left = right;
                right = tmp;
            }

            uint[] result = new uint[left.Length + 1];

            ulong carry = 0;
            int i = 0;

            for (; i < right.Length; i++)
            {
                ulong sum = (ulong)left[i] + right[i] + carry;
                result[i] = (uint)sum;
                carry = sum >> 32;
            }

            for (; i < left.Length; i++)
            {
                ulong sum = (ulong)left[i] + carry;
                result[i] = (uint)sum;
                carry = sum >> 32;

                if (carry == 0)
                {
                    i++;
                    break;
                }
            }

            for (; i < left.Length; i++)
                result[i] = left[i];

            result[left.Length] = (uint)carry;
            return result;
        }

        internal static uint[] Subtract(ReadOnlySpan<uint> left, uint right)
        {
            uint[] result = new uint[left.Length];

            ulong borrow = right;
            int i = 0;

            for (; i < left.Length; i++)
            {
                ulong current = left[i];
                ulong diff = current - borrow;

                result[i] = (uint)diff;

                borrow = current < borrow ? 1UL : 0UL;

                if (borrow == 0)
                {
                    i++;
                    break;
                }
            }

            for (; i < left.Length; i++)
                result[i] = left[i];

            return result;
        }
        internal static uint[] Subtract(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
        {
            uint[] result = new uint[left.Length];

            ulong borrow = 0;
            int i = 0;

            for (; i < right.Length; i++)
            {
                ulong subtrahend = (ulong)right[i] + borrow;
                ulong minuend = left[i];
                ulong diff = minuend - subtrahend;

                result[i] = (uint)diff;

                borrow = minuend < subtrahend ? 1UL : 0UL;
            }

            for (; i < left.Length; i++)
            {
                ulong minuend = left[i];
                ulong diff = minuend - borrow;

                result[i] = (uint)diff;

                borrow = minuend < borrow ? 1UL : 0UL;

                if (borrow == 0)
                {
                    i++;
                    break;
                }
            }

            for (; i < left.Length; i++)
                result[i] = left[i];

            return result;
        }

        internal static uint[] Multiply(ReadOnlySpan<uint> left, uint right)
        {
            int leftLength = GetLength(left);

            if (leftLength == 0 || right == 0)
                return Array.Empty<uint>();

            if (leftLength + 1 > BigInteger.MaxLength)
                throw new OverflowException();

            uint[] result = new uint[leftLength + 1];

            ulong carry = 0;

            for (int i = 0; i < leftLength; i++)
            {
                ulong product = ((ulong)left[i] * right) + carry;
                result[i] = (uint)product;
                carry = product >> 32;
            }

            result[leftLength] = (uint)carry;
            return result;
        }
        internal static uint[] Multiply(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
        {
            int leftLength = GetLength(left);
            int rightLength = GetLength(right);

            if (leftLength == 0 || rightLength == 0)
                return Array.Empty<uint>();

            if (leftLength < rightLength)
            {
                ReadOnlySpan<uint> tmp = left;
                left = right;
                right = tmp;

                int tmpLength = leftLength;
                leftLength = rightLength;
                rightLength = tmpLength;
            }

            long resultLengthLong = (long)leftLength + rightLength;
            if (resultLengthLong > BigInteger.MaxLength)
                throw new OverflowException();

            int resultLength = (int)resultLengthLong;
            uint[] result = new uint[resultLength];

            for (int i = 0; i < rightLength; i++)
            {
                ulong r = right[i];
                if (r == 0)
                    continue;

                ulong carry = 0;
                int resultIndex = i;

                for (int j = 0; j < leftLength; j++, resultIndex++)
                {
                    ulong product =
                        ((ulong)left[j] * r) +
                        result[resultIndex] +
                        carry;

                    result[resultIndex] = (uint)product;
                    carry = product >> 32;
                }

                result[i + leftLength] = (uint)carry;
            }

            return result;
        }

        internal static uint[] Divide(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
        {
            return Divide(left, right, out _);
        }
        internal static uint[] Divide(ReadOnlySpan<uint> left, uint right)
        {
            return Divide(left, right, out _);
        }
        internal static uint[] Remainder(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
        {
            Divide(left, right, out uint[] remainder);
            return remainder;
        }
        internal static uint Remainder(ReadOnlySpan<uint> left, uint right)
        {
            Divide(left, right, out uint remainder);
            return remainder;
        }

        internal static uint[] Divide(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right, out uint[] remainder)
        {
            int leftLength = GetLength(left);
            int rightLength = GetLength(right);

            if (rightLength == 0)
                throw new DivideByZeroException();

            if (leftLength == 0)
            {
                remainder = Array.Empty<uint>();
                return Array.Empty<uint>();
            }

            int cmp = Compare(left, right);
            if (cmp < 0)
            {
                remainder = Copy(left, leftLength);
                return Array.Empty<uint>();
            }

            if (cmp == 0)
            {
                remainder = Array.Empty<uint>();
                return new uint[] { 1u };
            }

            if (rightLength == 1)
            {
                uint rest;
                uint[] quotient = Divide(left, right[0], out rest);
                remainder = rest == 0 ? Array.Empty<uint>() : new uint[] { rest };
                return quotient;
            }

            return DivideMultiWord(left, leftLength, right, rightLength, out remainder);
        }

        internal static uint[] Divide(ReadOnlySpan<uint> left, uint right, out uint remainder)
        {
            if (right == 0)
                throw new DivideByZeroException();

            int leftLength = GetLength(left);
            if (leftLength == 0)
            {
                remainder = 0;
                return Array.Empty<uint>();
            }

            uint[] quotient = new uint[leftLength];
            ulong rem = 0;

            for (int i = leftLength - 1; i >= 0; i--)
            {
                ulong value = (rem << 32) | left[i];
                quotient[i] = (uint)(value / right);
                rem = value % right;
            }

            remainder = (uint)rem;
            return quotient;
        }
        private static uint[] DivideMultiWord(
            ReadOnlySpan<uint> dividend,
            int dividendLength,
            ReadOnlySpan<uint> divisor,
            int divisorLength,
            out uint[] remainder)
        {
            int shift = LeadingZeroCount(divisor[divisorLength - 1]);

            uint[] u = new uint[dividendLength + 1];
            uint[] v = new uint[divisorLength + 1];

            LeftShift(dividend, dividendLength, shift, u);
            LeftShift(divisor, divisorLength, shift, v);

            int quotientLength = dividendLength - divisorLength + 1;
            uint[] quotient = new uint[quotientLength];

            const ulong Base = 0x1_0000_0000UL;

            for (int j = quotientLength - 1; j >= 0; j--)
            {
                ulong qhat;
                ulong rhat;

                ulong high = u[j + divisorLength];
                ulong low = u[j + divisorLength - 1];
                ulong divisorHigh = v[divisorLength - 1];

                if (high == divisorHigh)
                {
                    qhat = uint.MaxValue;
                    rhat = low + divisorHigh;
                }
                else
                {
                    ulong value = (high << 32) | low;
                    qhat = value / divisorHigh;
                    rhat = value % divisorHigh;
                }

                if (divisorLength > 1)
                {
                    ulong divisorNext = v[divisorLength - 2];
                    ulong dividendNext = u[j + divisorLength - 2];

                    while (rhat < Base && qhat * divisorNext > ((rhat << 32) | dividendNext))
                    {
                        qhat--;
                        rhat += divisorHigh;
                    }
                }

                bool underflow = SubtractProduct(u, j, v, divisorLength, qhat);

                if (underflow)
                {
                    qhat--;
                    AddBack(u, j, v, divisorLength);
                }

                quotient[j] = (uint)qhat;
            }

            remainder = RightShift(u, divisorLength, shift);
            return quotient;
        }

        private static uint[] Copy(ReadOnlySpan<uint> source, int length)
        {
            if (length == 0)
                return Array.Empty<uint>();

            uint[] result = new uint[length];

            for (int i = 0; i < length; i++)
                result[i] = source[i];

            return result;
        }

        private static bool SubtractProduct(uint[] left, int leftOffset, uint[] right, int rightLength, ulong multiplier)
        {
            ulong carry = 0;
            ulong borrow = 0;

            for (int i = 0; i < rightLength; i++)
            {
                ulong product = (ulong)right[i] * multiplier + carry;
                carry = product >> 32;

                ulong subtrahend = (uint)product + borrow;
                ulong minuend = left[leftOffset + i];

                left[leftOffset + i] = (uint)(minuend - subtrahend);
                borrow = minuend < subtrahend ? 1UL : 0UL;
            }

            ulong high = left[leftOffset + rightLength];
            ulong finalSubtrahend = carry + borrow;

            left[leftOffset + rightLength] = (uint)(high - finalSubtrahend);
            return high < finalSubtrahend;
        }

        private static void AddBack(uint[] left, int leftOffset, uint[] right, int rightLength)
        {
            ulong carry = 0;

            for (int i = 0; i < rightLength; i++)
            {
                ulong sum = (ulong)left[leftOffset + i] + right[i] + carry;
                left[leftOffset + i] = (uint)sum;
                carry = sum >> 32;
            }

            left[leftOffset + rightLength] = (uint)((ulong)left[leftOffset + rightLength] + carry);
        }

        private static void LeftShift(ReadOnlySpan<uint> source, int sourceLength, int shift, uint[] destination)
        {
            if (destination.Length < sourceLength)
                throw new ArgumentException("Destination is too small.");

            if (shift == 0)
            {
                for (int i = 0; i < sourceLength; i++)
                    destination[i] = source[i];

                if (destination.Length > sourceLength)
                    destination[sourceLength] = 0;

                return;
            }

            int inverseShift = 32 - shift;
            uint carry = 0;

            for (int i = 0; i < sourceLength; i++)
            {
                uint value = source[i];
                destination[i] = (value << shift) | carry;
                carry = value >> inverseShift;
            }

            if (destination.Length > sourceLength)
                destination[sourceLength] = carry;
            else if (carry != 0)
                throw new OverflowException();
        }
        private static uint[] RightShift(uint[] source, int sourceLength, int shift)
        {
            uint[] result = new uint[sourceLength];

            if (shift == 0)
            {
                for (int i = 0; i < sourceLength; i++)
                    result[i] = source[i];

                return result;
            }

            int inverseShift = 32 - shift;
            uint carry = 0;

            for (int i = sourceLength - 1; i >= 0; i--)
            {
                uint value = source[i];
                result[i] = (value >> shift) | carry;
                carry = value << inverseShift;
            }

            return result;
        }

        private static int LeadingZeroCount(uint value)
        {
            if (value == 0)
                return 32;

            int count = 0;

            if ((value & 0xFFFF0000u) == 0)
            {
                count += 16;
                value <<= 16;
            }

            if ((value & 0xFF000000u) == 0)
            {
                count += 8;
                value <<= 8;
            }

            if ((value & 0xF0000000u) == 0)
            {
                count += 4;
                value <<= 4;
            }

            if ((value & 0xC0000000u) == 0)
            {
                count += 2;
                value <<= 2;
            }

            if ((value & 0x80000000u) == 0)
                count++;

            return count;
        }
    }
    internal static class NumericsHelpers
    {
        internal static ulong MakeUInt64(uint uHi, uint uLo)
        {
            return ((ulong)uHi << 32) | (ulong)uLo;
        }
        internal static uint Abs(int value)
        {
            if (value >= 0)
                return (uint)value;

            return (uint)(-((long)value));
        }
    }

    public struct Vector2
    {
        internal const int Alignment = 8;
        internal const int ElementCount = 2;

        public float X;
        public float Y;

        public Vector2(float value)
        {
            X = value; Y = value;
        }

        public Vector2(float x, float y)
        {
            X = x; Y = y;
        }
        public static Vector2 One
        {
            get => new Vector2(1.0f);
        }
        public static Vector2 Zero
        {
            get => new Vector2(0.0f);
        }
        public static Vector2 UnitX
        {
            get => new Vector2(1.0f, 0.0f);
        }
        public static Vector2 UnitY
        {
            get => new Vector2(0.0f, 1.0f);
        }
        public override string ToString()
        {
            return $"<{X.ToString()}, {Y.ToString()}>";
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator +(Vector2 left, Vector2 right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X += right.X;
            result.Y += right.Y;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator /(Vector2 left, Vector2 right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X /= right.X;
            result.Y /= right.Y;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator /(Vector2 value1, float value2)
        {
            var result = new Vector2(value1.X, value1.Y);
            result.X /= value2;
            result.Y /= value2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector2 left, Vector2 right) => left.X == right.X && left.Y == right.Y;

        public static bool operator !=(Vector2 left, Vector2 right) => !(left == right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator *(Vector2 left, Vector2 right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X *= right.X;
            result.Y *= right.Y;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator *(Vector2 left, float right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X *= right;
            result.Y *= right;
            return result;
        }

        public static Vector2 operator *(float left, Vector2 right)
        {
            var result = new Vector2(right.X, right.Y);
            result.X *= left;
            result.Y *= left;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(float value)
        {
            this.X *= value;
            this.Y *= value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator -(Vector2 left, Vector2 right)
        {
            var result = new Vector2(left.X, left.Y);
            result.X -= right.X;
            result.Y -= right.Y;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator -(Vector2 value) => new Vector2(-(value.X), -(value.Y));
    }
    public struct Vector3
    {
        internal const int Alignment = 8;
        internal const int ElementCount = 3;

        public float X = 0;
        public float Y = 0;
        public float Z = 0;

        public Vector3(float value)
        {
            X = value; Y = value; Z = value;
        }
        public Vector3(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }
        public static Vector3 One
        {
            get => new Vector3(1.0f);
        }
        public static Vector3 Zero
        {
            get => new Vector3(0f);
        }
        public static Vector3 UnitX
        {
            get => new Vector3(1.0f, 0f, 0f);
        }
        public static Vector3 UnitY
        {
            get => new Vector3(0.0f, 1.0f, 0f);
        }
        public static Vector3 UnitZ
        {
            get => new Vector3(0f, 0f, 1.0f);
        }
        public override string ToString()
        {
            return $"<{X.ToString()}, {Y.ToString()}, {Z.ToString()}>";
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator +(Vector3 left, Vector3 right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X += right.X;
            result.Y += right.Y;
            result.Z += right.Z;
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator /(Vector3 left, Vector3 right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X /= right.X;
            result.Y /= right.Y;
            result.Z /= right.Z;
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator /(Vector3 value1, float value2)
        {
            Vector3 result = new Vector3(value1.X, value1.Y, value1.Z);
            value1.X /= value2;
            value1.Y /= value2;
            value1.Z /= value2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector3 left, Vector3 right)
            => left.X == right.X && left.Y == right.Y && left.Z == right.Z;

        public static bool operator !=(Vector3 left, Vector3 right) => !(left == right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(Vector3 left, Vector3 right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X *= right.X;
            result.Y *= right.Y;
            result.Z *= right.Z;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(Vector3 left, float right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X *= right;
            result.Y *= right;
            result.Z *= right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(float left, Vector3 right)
        {
            Vector3 result = new Vector3(right.X, right.Y, right.Z);
            result.X *= left;
            result.Y *= left;
            result.Z *= left;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(float value)
        {
            this.X *= value;
            this.Y *= value;
            this.Z *= value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator -(Vector3 left, Vector3 right)
        {
            Vector3 result = new Vector3(left.X, left.Y, left.Z);
            result.X -= right.X;
            result.Y -= right.Y;
            result.Z -= right.Z;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator -(Vector3 value) => new Vector3(-(value.X), -(value.Y), -(value.Z));
    }
    public struct Vector4
    {
        internal const int Alignment = 16;
        internal const int ElementCount = 4;

        public float X;
        public float Y;
        public float Z;
        public float W;


        public Vector4(float value)
        {
            X = value; Y = value; Z = value; W = value;
        }

        public Vector4(Vector2 value, float z, float w)
        {
            X = value.X; Y = value.Y; Z = z; W = w;
        }

        public Vector4(Vector3 value, float w)
        {
            X = value.X; Y = value.Y; Z = value.Z; W = w;
        }

        public Vector4(float x, float y, float z, float w)
        {
            X = x; Y = y; Z = z; W = w;
        }
        public static Vector4 One
        {
            get => new Vector4(1.0f);
        }
        public static Vector4 Zero
        {
            get => new Vector4(0.0f);
        }
        public static Vector4 UnitX
        {
            get => new Vector4(1.0f, 0f, 0f, 0f);
        }
        public static Vector4 UnitY
        {
            get => new Vector4(0.0f, 1.0f, 0f, 0f);
        }
        public static Vector4 UnitZ
        {
            get => new Vector4(0f, 0f, 1.0f, 0f);
        }
        public static Vector4 UnitW
        {
            get => new Vector4(0f, 0f, 0f, 1.0f);
        }
        public override string ToString()
        {
            return $"<{X.ToString()}, {Y.ToString()}, {Z.ToString()}, {W.ToString()}>";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator +(Vector4 left, Vector4 right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X += right.X;
            result.Y += right.Y;
            result.Z += right.Z;
            result.W += right.W;
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator /(Vector4 left, Vector4 right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X /= right.X;
            result.Y /= right.Y;
            result.Z /= right.Z;
            result.W /= right.W;
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator /(Vector4 value1, float value2)
        {
            Vector4 result = new Vector4(value1.X, value1.Y, value1.Z, value1.W);
            value1.X /= value2;
            value1.Y /= value2;
            value1.Z /= value2;
            value1.W /= value2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector4 left, Vector4 right)
            => left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;

        public static bool operator !=(Vector4 left, Vector4 right) => !(left == right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(Vector4 left, Vector4 right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X *= right.X;
            result.Y *= right.Y;
            result.Z *= right.Z;
            result.W *= right.W;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(Vector4 left, float right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X *= right;
            result.Y *= right;
            result.Z *= right;
            result.W *= right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(float left, Vector4 right)
        {
            Vector4 result = new Vector4(right.X, right.Y, right.Z, right.W);
            result.X *= left;
            result.Y *= left;
            result.Z *= left;
            result.W *= left;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator *=(float value)
        {
            this.X *= value;
            this.Y *= value;
            this.Z *= value;
            this.W *= value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator -(Vector4 left, Vector4 right)
        {
            Vector4 result = new Vector4(left.X, left.Y, left.Z, left.W);
            result.X -= right.X;
            result.Y -= right.Y;
            result.Z -= right.Z;
            result.W -= right.W;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator -(Vector4 value) => new Vector4(-(value.X), -(value.Y), -(value.Z), -(value.W));
    }
}
namespace System.Collections
{
    public interface IEnumerable
    {
        IEnumerator GetEnumerator();
    }
    public interface IEnumerator
    {
        bool MoveNext();
        void Reset();
        object Current
        {
            get;
        }
    }

    public interface ICollection : IEnumerable
    {
        int Count { get; }
        bool IsSynchronized { get; }
        object SyncRoot { get; }
        void CopyTo(Array array, int index);
    }

    public interface IList : ICollection
    {
        object this[int index]
        {
            get;
            set;
        }

        int Add(object value);

        bool Contains(object value);

        void Clear();

        bool IsReadOnly
        { get; }


        bool IsFixedSize
        {
            get;
        }

        void Insert(int index, object value);

        void Remove(object value);
        void RemoveAt(int index);
    }
}
namespace System.Collections.Generic
{
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

    public class List<T> : IEnumerable<T>, IEnumerable, IReadOnlyList<T>
    {
        private const int DefaultCapacity = 4;

        internal T[] _items; // Do not rename
        internal int _size; // Do not rename
        internal int _version; // Do not rename

        private static readonly T[] s_emptyArray = new T[0];

        public List()
        {
            _items = s_emptyArray;
        }
        public List(int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException();

            if (capacity == 0)
                _items = s_emptyArray;
            else
                _items = new T[capacity];
        }

        public int Capacity
        {
            get => _items.Length;
            set
            {
                if (value < _size)
                {
                    throw new ArgumentOutOfRangeException();
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
        public int Count => _size;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_size)
                {
                    throw new ArgumentOutOfRangeException();
                }
                return _items[index];
            }

            set
            {
                if ((uint)index >= (uint)_size)
                {
                    throw new ArgumentOutOfRangeException();
                }
                _items[index] = value;
                _version++;
            }
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

        public int IndexOf(T item)
            => Array.IndexOf<T>(_items, item, 0, _size);

        public bool Contains(T item)
        {
            return _size != 0 && IndexOf(item) >= 0;
        }

        public void CopyTo(T[] array)
            => CopyTo(array, 0);

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
}