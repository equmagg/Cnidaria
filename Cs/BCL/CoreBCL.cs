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
        public virtual bool Equals(object obj) { return false; }
        public virtual int GetHashCode() { return 0; }
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
        public int Length
        {
            [RuntimeIntrinsic]
            [MethodImpl(MethodImplOptions.NoInlining)]
            get { return 0; }
        }
        public System.Collections.IEnumerator GetEnumerator()
        {
            return null;
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

            if (!_CopyImpl(sourceArray, sourceIndex, destinationArray, destinationIndex, length))
                throw new ArrayTypeMismatchException();
        }
        [RuntimeIntrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool _CopyImpl(Array sourceArray, int sourceIndex, Array destinationArray, int destinationIndex, int length)
            => false;
    }
    public abstract unsafe class Delegate
    {

    }
    public sealed class String
    {
        public const string Empty = "";
        public int Length
        {
            [RuntimeIntrinsic]
            [MethodImpl(MethodImplOptions.NoInlining)]
            get { return 0; }
        }
        [RuntimeIntrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal ref char GetRawStringData() { throw new NullReferenceException(); }
        [RuntimeIntrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public ref char GetPinnableReference() { throw new NullReferenceException(); }
        [RuntimeIntrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
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
        public ReadOnlySpan<char> AsSpan()
        {
            ref char r = ref GetPinnableReference();
            return new ReadOnlySpan<char>(ref r, Length);
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
        public int IndexOf(char value)
        {
            ref char src = ref GetPinnableReference();
            for (int i = 0; i < Length; i++)
                if (System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i) == value)
                    return i;
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
        private static bool IsLatin1(char c) => (uint)c < (uint)Latin1CharInfo.Length;
        private static System.Globalization.UnicodeCategory GetLatin1UnicodeCategory(char c)
            => (System.Globalization.UnicodeCategory)(Latin1CharInfo[c] & UnicodeCategoryMask);
    }

    public struct SByte
    {
        private readonly sbyte m_value;
        public const sbyte MaxValue = (sbyte)0x7F;
        public const sbyte MinValue = unchecked((sbyte)0x80);

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

        public static int Parse(String str)
        {
            return -1;
        }
        public static bool TryParse(String str, out int result)
        {
            result = -1;
            return false;
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

        [RuntimeIntrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string _DoubleToStringImpl(double value)
        {
            return string.Empty; // handled by VM intrinsic
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
    public interface IDisposable
    {
        void Dispose();
    }
    public interface IFormatProvider
    {
        object GetFormat(Type formatType);
    }
    // math
    public static class Math
    {
        public const double E = 2.7182818284590452354;
        public const double PI = 3.14159265358979323846;
        public const double Tau = 6.283185307179586476925;

        private const int maxRoundingDigits = 15;
        private const double doubleRoundLimit = 1e16d;
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
    [System.AttributeUsage(System.AttributeTargets.Parameter, Inherited = false)]
    public sealed class AllowNullAttribute : Attribute
    {
        public AllowNullAttribute()
        { }
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
    public sealed class RuntimeIntrinsicAttribute : Attribute
    {
        public RuntimeIntrinsicAttribute() { }
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

        [RuntimeIntrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
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
            char* buffer = stackalloc char[64];
            char* p = buffer;
            // Handle NaN
            if (value != value)
            {
                char* nan = stackalloc char[] { 'N', 'a', 'N', '\0' };
                _Write(nan);
                return;
            }
            // Handle Infinity
            if (value == ((double)1.0 / (double)(0.0)))
            {
                char* inf = stackalloc char[] { 'I', 'n', 'f', 'i', 'n', 'i', 't', 'y', '\0' };
                _Write(inf);
                return;
            }
            if (value == ((double)-1.0 / (double)(0.0)))
            {
                char* inf = stackalloc char[] { '-', 'I', 'n', 'f', 'i', 'n', 'i', 't', 'y', '\0' };
                _Write(inf);
                return;
            }

            // Sign
            if (value < 0)
            {
                *p++ = '-';
                value = -value;
            }

            // Integer part
            long integer = (long)value;
            double frac = value - integer;

            // Convert integer part
            char* intBuf = stackalloc char[21];
            char* end = intBuf + 20;
            char* ip = end;
            *ip = '\0';

            if (integer == 0)
            {
                *--ip = '0';
            }
            else
            {
                while (integer != 0)
                {
                    long digit = integer % 10;
                    integer /= 10;
                    *--ip = (char)('0' + digit);
                }
            }

            // Copy integer part
            while (*ip != '\0')
                *p++ = *ip++;

            // Fractional part
            const int precision = 6; // fixed precision
            if (precision > 0)
            {
                *p++ = '.';
                for (int i = 0; i < precision; i++)
                {
                    frac *= 10;
                    int digit = (int)frac;
                    *p++ = (char)('0' + digit);
                    frac -= digit;
                }
            }
            *p = '\0';

            _Write(buffer);
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
        public static void Write(Span<char> value) { _Write(value); }
        public static void Write(string value) { _Write(value); }

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
        public static void WriteLine(Span<char> value) { Write(value); Write('\n'); }
        public static unsafe void WriteLine(char* value) { Write(value); Write('\n'); }
        // convenience aliases
        public static void print(bool value) { Write(value); }
        public static void print(char value) { Write(value); }
        public static void print(int value) { Write(value); }
        public static void print(ulong value) { Write(value); }
        public static void print(double value) { Write(value); }
        public static void print(string value) { WriteLine(value); }
        // intrinsics
        [RuntimeIntrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static unsafe void _Write(char* value) { }
        [RuntimeIntrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void _Write(string value) { }
        [RuntimeIntrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void _Write(Span<char> value) { }
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
            return ref GetArrayDataReference<T>(array);
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static unsafe ref byte GetArrayDataReference(Array array)
        {
            //return ref Unsafe.AddByteOffset(ref Unsafe.As<RawData>(array).Data, 
            //    (nuint)RuntimeHelpers.GetMethodTable(array)->BaseSize - (nuint)(2 * sizeof(IntPtr)));
            return ref *(byte*)0;
        }
    }
}
namespace System.Runtime.CompilerServices
{
    public static class RuntimeHelpers
    {

    }

    public static unsafe class Unsafe
    {
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