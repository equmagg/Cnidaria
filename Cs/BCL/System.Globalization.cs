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
    internal static class UnicodeUtility
    {
        /// <summary>
        /// The Unicode replacement character U+FFFD.
        /// </summary>
        public const uint ReplacementChar = 0xFFFD;

        public static int GetPlane(uint codePoint)
        {
            return (int)(codePoint >> 16);
        }

        public static int GetUtf16SequenceLength(uint value)
        {
            value -= 0x10000;   // if value < 0x10000, high byte = 0xFF; else high byte = 0x00
            value += (2 << 24); // if value < 0x10000, high byte = 0x01; else high byte = 0x02
            value >>= 24;       // shift high byte down
            return (int)value;  // and return it
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetUtf16SurrogatesFromSupplementaryPlaneScalar(uint value, out char highSurrogateCodePoint, out char lowSurrogateCodePoint)
        {
            // This calculation comes from the Unicode specification, Table 3-5.

            highSurrogateCodePoint = (char)((value + ((0xD800u - 0x40u) << 10)) >> 10);
            lowSurrogateCodePoint = (char)((value & 0x3FFu) + 0xDC00u);
        }

        public static int GetUtf8SequenceLength(uint value)
        {
            int a = ((int)value - 0x0800) >> 31;

            value ^= 0xF800u;
            value -= 0xF880u;   // if scalar is 1 or 3 code units, high byte = 0xFF; else high byte = 0x00
            value += (4 << 24); // if scalar is 1 or 3 code units, high byte = 0x03; else high byte = 0x04
            value >>= 24;       // shift high byte down

            // Final return value:
            // - U+0000..U+007F => 3 + (-1) * 2 = 1
            // - U+0080..U+07FF => 4 + (-1) * 2 = 2
            // - U+0800..U+FFFF => 3 + ( 0) * 2 = 3
            // - U+10000+       => 4 + ( 0) * 2 = 4
            return (int)value + (a * 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAsciiCodePoint(uint value) => value <= 0x7Fu;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBmpCodePoint(uint value) => value <= 0xFFFFu;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsHighSurrogateCodePoint(uint value) => IsInRangeInclusive(value, 0xD800U, 0xDBFFU);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInRangeInclusive(uint value, uint lowerBound, uint upperBound) => (value - lowerBound) <= (upperBound - lowerBound);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLowSurrogateCodePoint(uint value) => IsInRangeInclusive(value, 0xDC00U, 0xDFFFU);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSurrogateCodePoint(uint value) => IsInRangeInclusive(value, 0xD800U, 0xDFFFU);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidCodePoint(uint codePoint) => codePoint <= 0x10FFFFU;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidUnicodeScalar(uint value)
        {
            return ((value - 0x110000u) ^ 0xD800u) >= 0xFFEF0800u;
        }
    }
    public sealed class TextInfo
    {
        private enum Tristate : byte
        {
            NotInitialized = 0,
            False = 1,
            True = 2
        }

        private bool _isReadOnly;

        private readonly string _cultureName;
        private readonly CultureData _cultureData;

        private bool HasEmptyCultureName { get { return _cultureName.Length == 0; } }

        // // Name of the text info we're using (ie: _cultureData.TextInfoName)
        private readonly string _textInfoName;

        private Tristate _isAsciiCasingSameAsInvariant = Tristate.NotInitialized;

        // Invariant text info
        internal static readonly TextInfo Invariant = new TextInfo(CultureData.Invariant, readOnly: true) { _isAsciiCasingSameAsInvariant = Tristate.True };

        internal TextInfo(CultureData cultureData)
        {
            _cultureData = cultureData;
            _cultureName = _cultureData.CultureName;
            //_textInfoName = _cultureData.TextInfoName;

        }

        private TextInfo(CultureData cultureData, bool readOnly)
            : this(cultureData)
        {
            SetReadOnlyState(readOnly);
        }

        internal void SetReadOnlyState(bool readOnly)
        {
            _isReadOnly = readOnly;
        }

        public string ToLower(string str)
        {
            if (str == null)
                throw new ArgumentNullException();

            string dstStr = String.FastAllocateString(str.Length);
            ref char dst = ref dstStr.GetRawStringData();
            ref char src = ref str.GetPinnableReference();

            for (int i = 0; i < str.Length; i++)
            {
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) =
                    Char.ToLowerInvariant(System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i));
            }
            return dstStr;

        }

        public string ToUpper(string str)
        {
            if (str == null)
                throw new ArgumentNullException();

            string dstStr = String.FastAllocateString(str.Length);
            ref char dst = ref dstStr.GetRawStringData();
            ref char src = ref str.GetPinnableReference();

            for (int i = 0; i < str.Length; i++)
            {
                System.Runtime.CompilerServices.Unsafe.Add<char>(ref dst, i) =
                    Char.ToLowerInvariant(System.Runtime.CompilerServices.Unsafe.Add<char>(ref src, i));
            }
            return dstStr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static char ToLowerInvariant(char c)
        {
            if (UnicodeUtility.IsAsciiCodePoint(c))
            {
                return ToLowerAsciiInvariant(c);
            }

            throw new NotSupportedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static char ToUpperInvariant(char c)
        {
            if (UnicodeUtility.IsAsciiCodePoint(c))
            {
                return ToUpperAsciiInvariant(c);
            }

            throw new NotSupportedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char ToLowerAsciiInvariant(char c)
        {
            if (Char.IsAsciiLetterUpper(c))
            {
                c = (char)(byte)(c | 0x20);
            }
            return c;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static char ToUpperAsciiInvariant(char c)
        {
            if (Char.IsAsciiLetterLower(c))
            {
                c = (char)(c & 0x5F); // = low 7 bits of ~0x20
            }
            return c;
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