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
            return System.Number.Int64ToString(m_value);
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
            return System.Number.DoubleToString((double)m_value);
        }
        public string ToString(System.Globalization.CultureInfo cultureInfo)
        {
            return System.Number.DoubleToString((double)m_value);
        }
        public string ToString(string format, System.Globalization.CultureInfo cultureInfo)
        {
            return System.Number.DoubleToString((double)m_value);
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
        internal static unsafe string Int64ToString(long value)
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
        internal static unsafe string UInt64ToString(ulong value)
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
        internal static unsafe string DoubleToString(double value)
        {
            if (value != value) return "NaN";
            if (value == ((double)1.0 / (double)0.0)) return "Infinity";
            if (value == ((double)-1.0 / (double)0.0)) return "-Infinity";
            if (value == 0.0) return "0";

            return _DoubleToStringImpl(value);
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static string _DoubleToStringImpl(double value)
        {
            return string.Empty;
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

    public readonly partial struct DateTime
    {
        internal readonly ulong _dateData;

        private const int DaysPerYear = 365;
        private const int DaysPer4Years = DaysPerYear * 4 + 1;       // 1461
        private const int DaysPer100Years = DaysPer4Years * 25 - 1;  // 36524
        private const int DaysPer400Years = DaysPer100Years * 4 + 1; // 146097

        private const int DaysTo1601 = DaysPer400Years * 4;          // 584388
        private const int DaysTo1899 = DaysPer400Years * 4 + DaysPer100Years * 3 - 367;
        internal const int DaysTo1970 = DaysPer400Years * 4 + DaysPer100Years * 3 + DaysPer4Years * 17 + DaysPerYear; // 719,162
        internal const int DaysTo10000 = DaysPer400Years * 25 - 366;  // 3652059

        internal const long MinTicks = 0;

        private const ulong TicksMask = 0x3FFFFFFFFFFFFFFF;
        private const ulong FlagsMask = 0xC000000000000000;
        private const long TicksCeiling = 0x4000000000000000;
        internal const ulong KindUtc = 0x4000000000000000;
        private const ulong KindLocal = 0x8000000000000000;
        private const ulong KindLocalAmbiguousDst = 0xC000000000000000;
        private const int KindShift = 62;

        public DateTime(long ticks)
        {
            //if ((ulong)ticks > MaxTicks) ThrowTicksOutOfRange();
            _dateData = (ulong)ticks;
        }

        private DateTime(ulong dateData)
        {
            //Debug.Assert((dateData & TicksMask) <= MaxTicks);
            _dateData = dateData;
        }
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
    }
}
namespace System.Runtime.CompilerServices
{
    public static class RuntimeHelpers
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static bool IsReferenceOrContainsReferences<T>() where T : allows ref struct => IsReferenceOrContainsReferences<T>();

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

        public abstract T[] Rent(int minimumLength);

        public abstract void Return(T[] array, bool clearArray = false);
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
        public static void WriteSingleLittleEndian(Span<byte> destination, float value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                //int tmp = ReverseEndianness(BitConverter.SingleToInt32Bits(value));
                System.Runtime.InteropServices.MemoryMarshal.Write<float>(destination, in value);
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
                //long tmp = ReverseEndianness(BitConverter.DoubleToInt64Bits(value));
                System.Runtime.InteropServices.MemoryMarshal.Write<double>(destination, in value);
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
                //short tmp = ReverseEndianness(value);
                System.Runtime.InteropServices.MemoryMarshal.Write<short>(destination, in value);
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
                //int tmp = ReverseEndianness(value); 
                System.Runtime.InteropServices.MemoryMarshal.Write<int>(destination, in value);
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
                //long tmp = ReverseEndianness(value);
                System.Runtime.InteropServices.MemoryMarshal.Write<long>(destination, in value);
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
                //ushort tmp = ReverseEndianness(value);
                System.Runtime.InteropServices.MemoryMarshal.Write<ushort>(destination, in value);
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
                //uint tmp = ReverseEndianness(value);
                System.Runtime.InteropServices.MemoryMarshal.Write<uint>(destination, in value);
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
                //ulong tmp = ReverseEndianness(value);
                System.Runtime.InteropServices.MemoryMarshal.Write<ulong>(destination, in value);
            }
            else
            {
                System.Runtime.InteropServices.MemoryMarshal.Write<ulong>(destination, in value);
            }
        }
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
}