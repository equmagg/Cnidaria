using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.Wasm;
using System.Runtime.Intrinsics.X86;
using System.Text.Unicode;

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

        /// <summary>
        /// Returns a Unicode scalar value from two code points representing a UTF-16 surrogate pair.
        /// </summary>
        public static uint GetScalarFromUtf16SurrogatePair(uint highSurrogateCodePoint, uint lowSurrogateCodePoint)
        {
            // This calculation comes from the Unicode specification, Table 3-5.
            // Need to remove the D800 marker from the high surrogate and the DC00 marker from the low surrogate,
            // then fix up the "wwww = uuuuu - 1" section of the bit distribution. The code is written as below
            // to become just two instructions: shl, lea.

            return (highSurrogateCodePoint << 10) + lowSurrogateCodePoint - ((0xD800U << 10) + 0xDC00U - (1 << 16));
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
    public abstract partial class Encoding
    {
        internal int _codePage;

        private bool _isReadOnly = true;

        internal EncoderFallback encoderFallback;

        internal DecoderFallback decoderFallback;

        protected Encoding() : this(0)
        {
        }

        protected Encoding(int codePage)
        {
            // Validate code page
            ArgumentOutOfRangeException.ThrowIfNegative(codePage);

            // Remember code page
            _codePage = codePage;

            // Use default encoder/decoder fallbacks
            this.SetDefaultFallbacks();
        }

        protected Encoding(int codePage, EncoderFallback? encoderFallback, DecoderFallback? decoderFallback)
        {
            // Validate code page
            ArgumentOutOfRangeException.ThrowIfNegative(codePage);

            // Remember code page
            _codePage = codePage;

            this.encoderFallback = encoderFallback ?? EncoderFallback.ReplacementFallback;
            this.decoderFallback = decoderFallback ?? DecoderFallback.ReplacementFallback;
        }

        [MemberNotNull(nameof(encoderFallback))]
        [MemberNotNull(nameof(decoderFallback))]
        internal virtual void SetDefaultFallbacks()
        {
            // For UTF-X encodings, we use a replacement fallback with an "\xFFFD" string,
            // For ASCII we use "?" replacement fallback, etc.
            encoderFallback = EncoderFallback.ReplacementFallback;
            decoderFallback = DecoderFallback.ReplacementFallback;
        }

        public virtual int CodePage => _codePage;

        public EncoderFallback EncoderFallback
        {
            get => encoderFallback;
            set
            {
                if (this.IsReadOnly)
                    throw new InvalidOperationException(SR.InvalidOperation_ReadOnly);

                ArgumentNullException.ThrowIfNull(value);

                encoderFallback = value;
            }
        }

        public DecoderFallback DecoderFallback
        {
            get => decoderFallback;
            set
            {
                if (this.IsReadOnly)
                    throw new InvalidOperationException(SR.InvalidOperation_ReadOnly);

                ArgumentNullException.ThrowIfNull(value);

                decoderFallback = value;
            }
        }

        public bool IsReadOnly
        {
            get => _isReadOnly;
            private protected set => _isReadOnly = value;
        }

        public abstract int GetMaxByteCount(int charCount);

        public abstract int GetMaxCharCount(int byteCount);

        public static Encoding UTF8 => UTF8Encoding.s_default;
    }
    public partial class UTF8Encoding : Encoding
    {
        private const int UTF8_CODEPAGE = 65001;

        /// <summary>
        /// Transcoding to UTF-8 bytes from UTF-16 input chars will result in a maximum 3:1 expansion.
        /// </summary>
        /// <remarks>
        /// Supplementary code points are expanded to UTF-8 from UTF-16 at a 4:2 ratio,
        /// so 3:1 is still the correct value for maximum expansion.
        /// </remarks>
        private const int MaxUtf8BytesPerChar = 3;

        internal static readonly UTF8EncodingSealed s_default = new UTF8EncodingSealed(encoderShouldEmitUTF8Identifier: true);

        private readonly bool _emitUTF8Identifier;

        private readonly bool _isThrowException;

        public UTF8Encoding() :
            base(UTF8_CODEPAGE)
        {
        }

        public UTF8Encoding(bool encoderShouldEmitUTF8Identifier) :
            this()
        {
            _emitUTF8Identifier = encoderShouldEmitUTF8Identifier;
        }

        public UTF8Encoding(bool encoderShouldEmitUTF8Identifier, bool throwOnInvalidBytes) :
            this(encoderShouldEmitUTF8Identifier)
        {
            _isThrowException = throwOnInvalidBytes;

            // Encoding's constructor already did this, but it'll be wrong if we're throwing exceptions
            if (_isThrowException)
                SetDefaultFallbacks();
        }

        internal sealed override void SetDefaultFallbacks()
        {
            // For UTF-X encodings, we use a replacement fallback with an empty string
            if (_isThrowException)
            {
                this.encoderFallback = EncoderFallback.ExceptionFallback;
                this.decoderFallback = DecoderFallback.ExceptionFallback;
            }
            else
            {
                this.encoderFallback = new EncoderReplacementFallback("\xFFFD");
                this.decoderFallback = new DecoderReplacementFallback("\xFFFD");
            }
        }

        public override int GetMaxByteCount(int charCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);

            // GetMaxByteCount assumes that the caller might have a stateful Encoder instance. If the
            // Encoder instance already has a captured high surrogate, then one of two things will
            // happen:
            //
            // - The next char is a low surrogate, at which point the two chars together result in 4
            //   UTF-8 bytes in the output; or
            // - The next char is not a low surrogate (or the input reaches EOF), at which point the
            //   standalone captured surrogate will go through the fallback routine.
            //
            // The second case is the worst-case scenario for expansion, so it's what we use for any
            // pessimistic "max byte count" calculation: assume there's a captured surrogate and that
            // it must fall back.

            long byteCount = (long)charCount + 1; // +1 to account for captured surrogate, per above

            if (EncoderFallback.MaxCharCount > 1)
                byteCount *= EncoderFallback.MaxCharCount;

            byteCount *= MaxUtf8BytesPerChar;

            if (byteCount > 0x7fffffff)
                throw new ArgumentOutOfRangeException(nameof(charCount), SR.ArgumentOutOfRange_GetByteCountOverflow);

            return (int)byteCount;
        }

        public override int GetMaxCharCount(int byteCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

            // GetMaxCharCount assumes that the caller might have a stateful Decoder instance. If the
            // Decoder instance already has a captured partial UTF-8 subsequence, then one of two
            // thngs will happen:
            //
            // - The next byte(s) won't complete the subsequence but will instead be consumed into
            //   the Decoder's internal state, resulting in no character output; or
            // - The next byte(s) will complete the subsequence, and the previously captured
            //   subsequence and the next byte(s) will result in 1 - 2 chars output; or
            // - The captured subsequence will be treated as a singular ill-formed subsequence, at
            //   which point the captured subsequence will go through the fallback routine.
            //   (See The Unicode Standard, Sec. 3.9 for more information on this.)
            //
            // The third case is the worst-case scenario for expansion, since it means 0 bytes of
            // new input could cause any existing captured state to expand via fallback. So it's
            // what we'll use for any pessimistic "max char count" calculation.

            long charCount = ((long)byteCount + 1); // +1 to account for captured subsequence, as above

            // Non-shortest form would fall back, so get max count from fallback.
            // So would 11... followed by 11..., so you could fall back every byte
            if (DecoderFallback.MaxCharCount > 1)
            {
                charCount *= DecoderFallback.MaxCharCount;
            }

            if (charCount > 0x7fffffff)
                throw new ArgumentOutOfRangeException(nameof(byteCount), SR.ArgumentOutOfRange_GetCharCountOverflow);

            return (int)charCount;
        }

        /// <summary>
        /// A special instance of <see cref="UTF8Encoding"/> that is initialized with "don't throw on invalid sequences;
        /// perform <see cref="Rune.ReplacementChar"/> substitution instead" semantics. This type allows for devirtualization
        /// of calls made directly off of <see cref="Encoding.UTF8"/>. See https://github.com/dotnet/coreclr/pull/9230.
        /// </summary>
        internal sealed class UTF8EncodingSealed : UTF8Encoding
        {
            /// <summary>
            /// Maximum number of input elements we'll allow for going through the fast one-pass stackalloc code paths.
            /// </summary>
            private const int MaxSmallInputElementCount = 32;

            public UTF8EncodingSealed(bool encoderShouldEmitUTF8Identifier) : base(encoderShouldEmitUTF8Identifier) { }

            public override int GetMaxByteCount(int charCount)
            {
                // This is a specialization of UTF8Encoding.GetMaxByteCount
                // with the assumption that the default replacement fallback
                // emits 3 fallback bytes ([ EF BF BD ] = '\uFFFD') per
                // malformed input char in the worst case.

                if ((uint)charCount > (int.MaxValue / MaxUtf8BytesPerChar) - 1)
                {
                    // Move the throw out of the hot path to allow for inlining.
                    ThrowArgumentException(charCount);
                    static void ThrowArgumentException(int charCount)
                    {
                        throw new ArgumentOutOfRangeException(
                            paramName: nameof(charCount),
                            message: (charCount < 0) ? SR.ArgumentOutOfRange_NeedNonNegNum : SR.ArgumentOutOfRange_GetByteCountOverflow);
                    }
                }

                return (charCount * MaxUtf8BytesPerChar) + MaxUtf8BytesPerChar;
            }

            public override int GetMaxCharCount(int byteCount)
            {
                // This is a specialization of UTF8Encoding.GetMaxCharCount
                // with the assumption that the default replacement fallback
                // emits one fallback char ('\uFFFD') per malformed input
                // byte in the worst case.

                if ((uint)byteCount > int.MaxValue - 1)
                {
                    // Move the throw out of the hot path to allow for inlining.
                    ThrowArgumentException(byteCount);
                    static void ThrowArgumentException(int byteCount)
                    {
                        throw new ArgumentOutOfRangeException(
                            paramName: nameof(byteCount),
                            message: (byteCount < 0) ? SR.ArgumentOutOfRange_NeedNonNegNum : SR.ArgumentOutOfRange_GetCharCountOverflow);
                    }
                }

                return byteCount + 1;
            }
        }
    }
    public abstract class EncoderFallback
    {
        public static EncoderFallback ReplacementFallback => EncoderReplacementFallback.s_default;

        public static EncoderFallback ExceptionFallback => EncoderExceptionFallback.s_default;

        public abstract int MaxCharCount { get; }
    }
    public sealed class EncoderReplacementFallback : EncoderFallback
    {
        internal static readonly EncoderReplacementFallback s_default = new EncoderReplacementFallback();

        private readonly string _strDefault;

        public EncoderReplacementFallback() : this("?")
        {
        }

        public EncoderReplacementFallback(string replacement)
        {
            ArgumentNullException.ThrowIfNull(replacement);

            // Make sure it doesn't have bad surrogate pairs
            bool bFoundHigh = false;
            foreach (char c in replacement)
            {
                // Found a surrogate?
                if (char.IsSurrogate(c))
                {
                    // High or Low?
                    if (char.IsHighSurrogate(c))
                    {
                        // if already had a high one, stop
                        if (bFoundHigh)
                            break;  // break & throw at the bFoundHIgh below
                        bFoundHigh = true;
                    }
                    else
                    {
                        // Low, did we have a high?
                        if (!bFoundHigh)
                        {
                            // Didn't have one, make if fail when we stop
                            bFoundHigh = true;
                            break;
                        }

                        // Clear flag
                        bFoundHigh = false;
                    }
                }
                // If last was high we're in trouble (not surrogate so not low surrogate, so break)
                else if (bFoundHigh)
                    break;
            }
            if (bFoundHigh)
                throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex, nameof(replacement));

            _strDefault = replacement;
        }

        public string DefaultString => _strDefault;

        public override int MaxCharCount => _strDefault.Length;
    }
    public sealed class EncoderExceptionFallback : EncoderFallback
    {
        internal static readonly EncoderExceptionFallback s_default = new EncoderExceptionFallback();

        public EncoderExceptionFallback()
        {
        }

        public override int MaxCharCount => 0;

        public override bool Equals([NotNullWhen(true)] object? value) =>
            value is EncoderExceptionFallback;

        public override int GetHashCode() => 654;
    }
    public abstract class DecoderFallback
    {
        public static DecoderFallback ReplacementFallback => DecoderReplacementFallback.s_default;

        public static DecoderFallback ExceptionFallback => DecoderExceptionFallback.s_default;

        public abstract int MaxCharCount { get; }
    }
    public sealed class DecoderReplacementFallback : DecoderFallback
    {
        internal static readonly DecoderReplacementFallback s_default = new DecoderReplacementFallback();

        private readonly string _strDefault;

        public DecoderReplacementFallback() : this("?")
        {
        }

        public DecoderReplacementFallback(string replacement)
        {
            ArgumentNullException.ThrowIfNull(replacement);

            // Make sure it doesn't have bad surrogate pairs
            bool bFoundHigh = false;
            foreach (char c in replacement)
            {
                // Found a surrogate?
                if (char.IsSurrogate(c))
                {
                    // High or Low?
                    if (char.IsHighSurrogate(c))
                    {
                        // if already had a high one, stop
                        if (bFoundHigh)
                            break;  // break & throw at the bFoundHIgh below
                        bFoundHigh = true;
                    }
                    else
                    {
                        // Low, did we have a high?
                        if (!bFoundHigh)
                        {
                            // Didn't have one, make if fail when we stop
                            bFoundHigh = true;
                            break;
                        }

                        // Clear flag
                        bFoundHigh = false;
                    }
                }
                // If last was high we're in trouble (not surrogate so not low surrogate, so break)
                else if (bFoundHigh)
                    break;
            }
            if (bFoundHigh)
                throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex, nameof(replacement));

            _strDefault = replacement;
        }

        public string DefaultString => _strDefault;

        public override int MaxCharCount => _strDefault.Length;
    }
    public sealed class DecoderExceptionFallback : DecoderFallback
    {
        internal static readonly DecoderExceptionFallback s_default = new DecoderExceptionFallback();

        public override int MaxCharCount => 0;

        public override bool Equals([NotNullWhen(true)] object? value) =>
            value is DecoderExceptionFallback;

        public override int GetHashCode() => 879;
    }
    /// <summary>
    /// Represents a Unicode scalar value ([ U+0000..U+D7FF ], inclusive; or [ U+E000..U+10FFFF ], inclusive).
    /// </summary>
    /// <remarks>
    /// This type's constructors and conversion operators validate the input, so consumers can call the APIs
    /// assuming that the underlying <see cref="Rune"/> instance is well-formed.
    /// </remarks>
    public readonly struct Rune : IComparable, IComparable<Rune>, IEquatable<Rune>
    {
        private readonly uint _value;

        private Rune(uint scalarValue, bool _)
        {
            _value = scalarValue;
        }

        public static bool operator ==(Rune left, Rune right) => left._value == right._value;

        public static bool operator !=(Rune left, Rune right) => left._value != right._value;

        /// <summary>
        /// A <see cref="Rune"/> instance that represents the Unicode replacement character U+FFFD.
        /// </summary>
        public static Rune ReplacementChar => UnsafeCreate(UnicodeUtility.ReplacementChar);

        /// <summary>
        /// Returns the Unicode scalar value as an integer.
        /// </summary>
        public int Value => (int)_value;

        public int CompareTo(Rune other) => this.Value - other.Value; // values don't span entire 32-bit domain; won't integer overflow

        /// <inheritdoc cref="IComparable.CompareTo" />
        int IComparable.CompareTo(object? obj)
        {
            if (obj is null)
            {
                return 1; // non-null ("this") always sorts after null
            }

            if (obj is Rune other)
            {
                return this.CompareTo(other);
            }

            throw new ArgumentException();
        }

        /// <summary>
        /// Decodes the <see cref="Rune"/> at the beginning of the provided UTF-8 source buffer.
        /// </summary>
        /// <returns>
        /// <para>
        /// If the source buffer begins with a valid UTF-8 encoded scalar value, returns <see cref="OperationStatus.Done"/>,
        /// and outs via <paramref name="result"/> the decoded <see cref="Rune"/> and via <paramref name="bytesConsumed"/> the
        /// number of <see langword="byte"/>s used in the input buffer to encode the <see cref="Rune"/>.
        /// </para>
        /// <para>
        /// If the source buffer is empty or contains only a partial UTF-8 subsequence, returns <see cref="OperationStatus.NeedMoreData"/>,
        /// and outs via <paramref name="result"/> <see cref="ReplacementChar"/> and via <paramref name="bytesConsumed"/> the length of the input buffer.
        /// </para>
        /// <para>
        /// If the source buffer begins with an ill-formed UTF-8 encoded scalar value, returns <see cref="OperationStatus.InvalidData"/>,
        /// and outs via <paramref name="result"/> <see cref="ReplacementChar"/> and via <paramref name="bytesConsumed"/> the number of
        /// <see langword="char"/>s used in the input buffer to encode the ill-formed sequence.
        /// </para>
        /// </returns>
        /// <remarks>
        /// The general calling convention is to call this method in a loop, slicing the <paramref name="source"/> buffer by
        /// <paramref name="bytesConsumed"/> elements on each iteration of the loop. On each iteration of the loop <paramref name="result"/>
        /// will contain the real scalar value if successfully decoded, or it will contain <see cref="ReplacementChar"/> if
        /// the data could not be successfully decoded. This pattern provides convenient automatic U+FFFD substitution of
        /// invalid sequences while iterating through the loop.
        /// </remarks>
        public static OperationStatus DecodeFromUtf8(ReadOnlySpan<byte> source, out Rune result, out int bytesConsumed)
        {
            // This method follows the Unicode Standard's recommendation for detecting
            // the maximal subpart of an ill-formed subsequence. See The Unicode Standard,
            // Ch. 3.9 for more details. In summary, when reporting an invalid subsequence,
            // it tries to consume as many code units as possible as long as those code
            // units constitute the beginning of a longer well-formed subsequence per Table 3-7.

            // Try reading source[0].

            int index = 0;
            if (source.IsEmpty)
            {
                goto NeedsMoreData;
            }

            uint tempValue = source[0];
            if (UnicodeUtility.IsAsciiCodePoint(tempValue))
            {
                bytesConsumed = 1;
                result = UnsafeCreate(tempValue);
                return OperationStatus.Done;
            }

            // Per Table 3-7, the beginning of a multibyte sequence must be a code unit in
            // the range [C2..F4]. If it's outside of that range, it's either a standalone
            // continuation byte, or it's an overlong two-byte sequence, or it's an out-of-range
            // four-byte sequence.

            // Try reading source[1].

            index = 1;
            if (!UnicodeUtility.IsInRangeInclusive(tempValue, 0xC2, 0xF4))
            {
                goto Invalid;
            }

            tempValue = (tempValue - 0xC2) << 6;

            if (source.Length <= 1)
            {
                goto NeedsMoreData;
            }

            // Continuation bytes are of the form [10xxxxxx], which means that their two's
            // complement representation is in the range [-65..-128]. This allows us to
            // perform a single comparison to see if a byte is a continuation byte.

            int thisByteSignExtended = (sbyte)source[1];
            if (thisByteSignExtended >= -64)
            {
                goto Invalid;
            }

            tempValue += (uint)thisByteSignExtended;
            tempValue += 0x80; // remove the continuation byte marker
            tempValue += (0xC2 - 0xC0) << 6; // remove the leading byte marker

            if (tempValue < 0x0800)
            {
                goto Finish; // this is a valid 2-byte sequence
            }

            // This appears to be a 3- or 4-byte sequence. Since per Table 3-7 we now have
            // enough information (from just two code units) to detect overlong or surrogate
            // sequences, we need to perform these checks now.

            if (!UnicodeUtility.IsInRangeInclusive(tempValue, ((0xE0 - 0xC0) << 6) + (0xA0 - 0x80), ((0xF4 - 0xC0) << 6) + (0x8F - 0x80)))
            {
                // The first two bytes were not in the range [[E0 A0]..[F4 8F]].
                // This is an overlong 3-byte sequence or an out-of-range 4-byte sequence.
                goto Invalid;
            }

            if (UnicodeUtility.IsInRangeInclusive(tempValue, ((0xED - 0xC0) << 6) + (0xA0 - 0x80), ((0xED - 0xC0) << 6) + (0xBF - 0x80)))
            {
                // This is a UTF-16 surrogate code point, which is invalid in UTF-8.
                goto Invalid;
            }

            if (UnicodeUtility.IsInRangeInclusive(tempValue, ((0xF0 - 0xC0) << 6) + (0x80 - 0x80), ((0xF0 - 0xC0) << 6) + (0x8F - 0x80)))
            {
                // This is an overlong 4-byte sequence.
                goto Invalid;
            }

            // The first two bytes were just fine. We don't need to perform any other checks
            // on the remaining bytes other than to see that they're valid continuation bytes.

            // Try reading source[2].

            index = 2;
            if (source.Length <= 2)
            {
                goto NeedsMoreData;
            }

            thisByteSignExtended = (sbyte)source[2];
            if (thisByteSignExtended >= -64)
            {
                goto Invalid; // this byte is not a UTF-8 continuation byte
            }

            tempValue <<= 6;
            tempValue += (uint)thisByteSignExtended;
            tempValue += 0x80; // remove the continuation byte marker
            tempValue -= (0xE0 - 0xC0) << 12; // remove the leading byte marker

            if (tempValue <= 0xFFFF)
            {
                goto Finish; // this is a valid 3-byte sequence
            }

            // Try reading source[3].

            index = 3;
            if (source.Length <= 3)
            {
                goto NeedsMoreData;
            }

            thisByteSignExtended = (sbyte)source[3];
            if (thisByteSignExtended >= -64)
            {
                goto Invalid; // this byte is not a UTF-8 continuation byte
            }

            tempValue <<= 6;
            tempValue += (uint)thisByteSignExtended;
            tempValue += 0x80; // remove the continuation byte marker
            tempValue -= (0xF0 - 0xE0) << 18; // remove the leading byte marker

            // Valid 4-byte sequence

        Finish:

            bytesConsumed = index + 1;
// Valid subsequences are always length [1..4]
            result = UnsafeCreate(tempValue);
            return OperationStatus.Done;

        NeedsMoreData:

// Incomplete subsequences are always length 0..3
            bytesConsumed = index;
            result = ReplacementChar;
            return OperationStatus.NeedMoreData;

        Invalid:

// Invalid subsequences are always length 1..3
            bytesConsumed = index;
            result = ReplacementChar;
            return OperationStatus.InvalidData;
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => (obj is Rune other) && Equals(other);

        public bool Equals(Rune other) => this == other;

        public override int GetHashCode() => Value;

        /// <summary>
        /// Creates a <see cref="Rune"/> without performing validation on the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Rune UnsafeCreate(uint scalarValue) => new Rune(scalarValue, false);
    }
    internal ref partial struct ValueStringBuilder
    {
        private char[]? _arrayToReturnToPool;
        private Span<char> _chars;
        private int _pos;

        public ValueStringBuilder(Span<char> initialBuffer)
        {
            _arrayToReturnToPool = null;
            _chars = initialBuffer;
            _pos = 0;
        }

        public ValueStringBuilder(int initialCapacity)
        {
            _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(initialCapacity);
            _chars = _arrayToReturnToPool;
            _pos = 0;
        }

        public int Length
        {
            get => _pos;
            set
            {
                _pos = value;
            }
        }

        public int Capacity => _chars.Length;

        public void EnsureCapacity(int capacity)
        {
            // This is not expected to be called this with negative capacity

            // If the caller has a bug and calls this with negative capacity, make sure to call Grow to throw an exception.
            if ((uint)capacity > (uint)_chars.Length)
                Grow(capacity - _pos);
        }

        /// <summary>
        /// Get a pinnable reference to the builder.
        /// Does not ensure there is a null char after <see cref="Length"/>
        /// This overload is pattern matched in the C# 7.3+ compiler so you can omit
        /// the explicit method call, and write eg "fixed (char* c = builder)"
        /// </summary>
        public ref char GetPinnableReference()
        {
            return ref MemoryMarshal.GetReference(_chars);
        }

        /// <summary>
        /// Get a pinnable reference to the builder.
        /// </summary>
        /// <param name="terminate">Ensures that the builder has a null char after <see cref="Length"/></param>
        public ref char GetPinnableReference(bool terminate)
        {
            if (terminate)
            {
                EnsureCapacity(Length + 1);
                _chars[Length] = '\0';
            }
            return ref MemoryMarshal.GetReference(_chars);
        }

        public ref char this[int index]
        {
            get
            {
                return ref _chars[index];
            }
        }

        public override string ToString()
        {
            string s = _chars.Slice(0, _pos).ToString();
            Dispose();
            return s;
        }

        /// <summary>Returns the underlying storage of the builder.</summary>
        public Span<char> RawChars => _chars;

        /// <summary>
        /// Returns a span around the contents of the builder.
        /// </summary>
        /// <param name="terminate">Ensures that the builder has a null char after <see cref="Length"/></param>
        public ReadOnlySpan<char> AsSpan(bool terminate)
        {
            if (terminate)
            {
                EnsureCapacity(Length + 1);
                _chars[Length] = '\0';
            }
            return _chars.Slice(0, _pos);
        }

        public ReadOnlySpan<char> AsSpan() => _chars.Slice(0, _pos);
        public ReadOnlySpan<char> AsSpan(int start) => _chars.Slice(start, _pos - start);
        public ReadOnlySpan<char> AsSpan(int start, int length) => _chars.Slice(start, length);

        public bool TryCopyTo(Span<char> destination, out int charsWritten)
        {
            if (_chars.Slice(0, _pos).TryCopyTo(destination))
            {
                charsWritten = _pos;
                Dispose();
                return true;
            }
            else
            {
                charsWritten = 0;
                Dispose();
                return false;
            }
        }

        public void Insert(int index, char value, int count)
        {
            if (_pos > _chars.Length - count)
            {
                Grow(count);
            }

            int remaining = _pos - index;
            _chars.Slice(index, remaining).CopyTo(_chars.Slice(index + count));
            _chars.Slice(index, count).Fill(value);
            _pos += count;
        }

        public void Insert(int index, string? s)
        {
            if (s == null)
            {
                return;
            }

            int count = s.Length;

            if (_pos > (_chars.Length - count))
            {
                Grow(count);
            }

            int remaining = _pos - index;
            _chars.Slice(index, remaining).CopyTo(_chars.Slice(index + count));
            s
                .CopyTo(_chars.Slice(index));
            _pos += count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(char c)
        {
            int pos = _pos;
            Span<char> chars = _chars;
            if ((uint)pos < (uint)chars.Length)
            {
                chars[pos] = c;
                _pos = pos + 1;
            }
            else
            {
                GrowAndAppend(c);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(string? s)
        {
            if (s == null)
            {
                return;
            }

            int pos = _pos;
            if (s.Length == 1 && (uint)pos < (uint)_chars.Length) // very common case, e.g. appending strings from NumberFormatInfo like separators, percent symbols, etc.
            {
                _chars[pos] = s[0];
                _pos = pos + 1;
            }
            else
            {
                AppendSlow(s);
            }
        }

        private void AppendSlow(string s)
        {
            int pos = _pos;
            if (pos > _chars.Length - s.Length)
            {
                Grow(s.Length);
            }

            s
                .CopyTo(_chars.Slice(pos));
            _pos += s.Length;
        }

        public void Append(char c, int count)
        {
            if (_pos > _chars.Length - count)
            {
                Grow(count);
            }

            Span<char> dst = _chars.Slice(_pos, count);
            for (int i = 0; i < dst.Length; i++)
            {
                dst[i] = c;
            }
            _pos += count;
        }

        public void Append(scoped ReadOnlySpan<char> value)
        {
            int pos = _pos;
            if (pos > _chars.Length - value.Length)
            {
                Grow(value.Length);
            }

            value.CopyTo(_chars.Slice(_pos));
            _pos += value.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<char> AppendSpan(int length)
        {
            int origPos = _pos;
            if (origPos > _chars.Length - length)
            {
                Grow(length);
            }

            _pos = origPos + length;
            return _chars.Slice(origPos, length);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowAndAppend(char c)
        {
            Grow(1);
            Append(c);
        }

        /// <summary>
        /// Resize the internal buffer either by doubling current buffer size or
        /// by adding <paramref name="additionalCapacityBeyondPos"/> to
        /// <see cref="_pos"/> whichever is greater.
        /// </summary>
        /// <param name="additionalCapacityBeyondPos">
        /// Number of chars requested beyond current position.
        /// </param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow(int additionalCapacityBeyondPos)
        {
            const uint ArrayMaxLength = 0x7FFFFFC7; // same as Array.MaxLength

            // Increase to at least the required size (_pos + additionalCapacityBeyondPos), but try
            // to double the size if possible, bounding the doubling to not go beyond the max array length.
            int newCapacity = (int)Math.Max(
                (uint)(_pos + additionalCapacityBeyondPos),
                Math.Min((uint)_chars.Length * 2, ArrayMaxLength));

            // Make sure to let Rent throw an exception if the caller has a bug and the desired capacity is negative.
            // This could also go negative if the actual required length wraps around.
            char[] poolArray = ArrayPool<char>.Shared.Rent(newCapacity);

            _chars.Slice(0, _pos).CopyTo(poolArray);

            char[]? toReturn = _arrayToReturnToPool;
            _chars = _arrayToReturnToPool = poolArray;
            if (toReturn != null)
            {
                ArrayPool<char>.Shared.Return(toReturn);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            char[]? toReturn = _arrayToReturnToPool;
            this = default; // for safety, to avoid using pooled array if this instance is erroneously appended to again
            if (toReturn != null)
            {
                ArrayPool<char>.Shared.Return(toReturn);
            }
        }
    }
    internal ref partial struct ValueStringBuilder
    {
        // Copied from StringBuilder, can't be done via generic extension
        // as ValueStringBuilder is a ref struct and cannot be used in a generic.
        internal void AppendFormatHelper(IFormatProvider? provider, string format, ReadOnlySpan<object?> args)
        {
            ArgumentNullException.ThrowIfNull(format);

            // Undocumented exclusive limits on the range for Argument Hole Index and Argument Hole Alignment.
            const int IndexLimit = 1_000_000; // Note:            0 <= ArgIndex < IndexLimit
            const int WidthLimit = 1_000_000; // Note:  -WidthLimit <  ArgAlign < WidthLimit

            // Query the provider (if one was supplied) for an ICustomFormatter.  If there is one,
            // it needs to be used to transform all arguments.
            ICustomFormatter? cf = (ICustomFormatter?)provider?.GetFormat(typeof(ICustomFormatter));

            // Repeatedly find the next hole and process it.
            int pos = 0;
            char ch;
            while (true)
            {
                // Skip until either the end of the input or the first unescaped opening brace, whichever comes first.
                // Along the way we need to also unescape escaped closing braces.
                while (true)
                {
                    // Find the next brace.  If there isn't one, the remainder of the input is text to be appended, and we're done.
                    if ((uint)pos >= (uint)format.Length)
                    {
                        return;
                    }

                    ReadOnlySpan<char> remainder = format.AsSpan(pos);
                    int countUntilNextBrace = remainder.IndexOfAny('{', '}');
                    if (countUntilNextBrace < 0)
                    {
                        Append(remainder);
                        return;
                    }

                    // Append the text until the brace.
                    Append(remainder.Slice(0, countUntilNextBrace));
                    pos += countUntilNextBrace;

                    // Get the brace.  It must be followed by another character, either a copy of itself in the case of being
                    // escaped, or an arbitrary character that's part of the hole in the case of an opening brace.
                    char brace = format[pos];
                    ch = MoveNext(format, ref pos);
                    if (brace == ch)
                    {
                        Append(ch);
                        pos++;
                        continue;
                    }

                    // This wasn't an escape, so it must be an opening brace.
                    if (brace != '{')
                    {
                        ThrowHelper.ThrowFormatInvalidString(pos, ExceptionResource.Format_UnexpectedClosingBrace);
                    }

                    // Proceed to parse the hole.
                    break;
                }

                // We're now positioned just after the opening brace of an argument hole, which consists of
                // an opening brace, an index, an optional width preceded by a comma, and an optional format
                // preceded by a colon, with arbitrary amounts of spaces throughout.
                int width = 0;
                bool leftJustify = false;
                ReadOnlySpan<char> itemFormatSpan = default; // used if itemFormat is null

                // First up is the index parameter, which is of the form:
                //     at least on digit
                //     optional any number of spaces
                // We've already read the first digit into ch.
                int index = ch - '0';
                if ((uint)index >= 10u)
                {
                    ThrowHelper.ThrowFormatInvalidString(pos, ExceptionResource.Format_ExpectedAsciiDigit);
                }

                // Common case is a single digit index followed by a closing brace.  If it's not a closing brace,
                // proceed to finish parsing the full hole format.
                ch = MoveNext(format, ref pos);
                if (ch != '}')
                {
                    // Continue consuming optional additional digits.
                    while (char.IsAsciiDigit(ch) && index < IndexLimit)
                    {
                        index = index * 10 + ch - '0';
                        ch = MoveNext(format, ref pos);
                    }

                    // Consume optional whitespace.
                    while (ch == ' ')
                    {
                        ch = MoveNext(format, ref pos);
                    }

                    // Parse the optional alignment, which is of the form:
                    //     comma
                    //     optional any number of spaces
                    //     optional -
                    //     at least one digit
                    //     optional any number of spaces
                    if (ch == ',')
                    {
                        // Consume optional whitespace.
                        do
                        {
                            ch = MoveNext(format, ref pos);
                        }
                        while (ch == ' ');

                        // Consume an optional minus sign indicating left alignment.
                        if (ch == '-')
                        {
                            leftJustify = true;
                            ch = MoveNext(format, ref pos);
                        }

                        // Parse alignment digits. The read character must be a digit.
                        width = ch - '0';
                        if ((uint)width >= 10u)
                        {
                            ThrowHelper.ThrowFormatInvalidString(pos, ExceptionResource.Format_ExpectedAsciiDigit);
                        }
                        ch = MoveNext(format, ref pos);
                        while (char.IsAsciiDigit(ch) && width < WidthLimit)
                        {
                            width = width * 10 + ch - '0';
                            ch = MoveNext(format, ref pos);
                        }

                        // Consume optional whitespace
                        while (ch == ' ')
                        {
                            ch = MoveNext(format, ref pos);
                        }
                    }

                    // The next character needs to either be a closing brace for the end of the hole,
                    // or a colon indicating the start of the format.
                    if (ch != '}')
                    {
                        if (ch != ':')
                        {
                            // Unexpected character
                            ThrowHelper.ThrowFormatInvalidString(pos, ExceptionResource.Format_UnclosedFormatItem);
                        }

                        // Search for the closing brace; everything in between is the format,
                        // but opening braces aren't allowed.
                        int startingPos = pos;
                        while (true)
                        {
                            ch = MoveNext(format, ref pos);

                            if (ch == '}')
                            {
                                // Argument hole closed
                                break;
                            }

                            if (ch == '{')
                            {
                                // Braces inside the argument hole are not supported
                                ThrowHelper.ThrowFormatInvalidString(pos, ExceptionResource.Format_UnclosedFormatItem);
                            }
                        }

                        startingPos++;
                        itemFormatSpan = format.AsSpan(startingPos, pos - startingPos);
                    }
                }

                // Construct the output for this arg hole.
                pos++;
                string? s = null;
                string? itemFormat = null;

                if ((uint)index >= (uint)args.Length)
                {
                    ThrowHelper.ThrowFormatIndexOutOfRange();
                }
                object? arg = args[index];

                if (cf != null)
                {
                    if (!itemFormatSpan.IsEmpty)
                    {
                        itemFormat = new string(itemFormatSpan);
                    }

                    s = cf.Format(itemFormat, arg, provider);
                }

                if (s == null)
                {
                    // If arg is ISpanFormattable and the beginning doesn't need padding,
                    // try formatting it into the remaining current chunk.
                    if ((leftJustify || width == 0) &&
                        arg is ISpanFormattable spanFormattableArg &&
                        spanFormattableArg.TryFormat(_chars.Slice(_pos), out int charsWritten, itemFormatSpan, provider))
                    {
                        _pos += charsWritten;

                        // Pad the end, if needed.
                        if (leftJustify && width > charsWritten)
                        {
                            Append(' ', width - charsWritten);
                        }

                        // Continue to parse other characters.
                        continue;
                    }

                    // Otherwise, fallback to trying IFormattable or calling ToString.
                    if (arg is IFormattable formattableArg)
                    {
                        if (itemFormatSpan.Length != 0)
                        {
                            itemFormat ??= new string(itemFormatSpan);
                        }
                        s = formattableArg.ToString(itemFormat, provider);
                    }
                    else
                    {
                        s = arg?.ToString();
                    }

                    s ??= string.Empty;
                }

                // Append it to the final output of the Format String.
                if (width <= s.Length)
                {
                    Append(s);
                }
                else if (leftJustify)
                {
                    Append(s);
                    Append(' ', width - s.Length);
                }
                else
                {
                    Append(' ', width - s.Length);
                    Append(s);
                }

                // Continue parsing the rest of the format string.
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static char MoveNext(string format, ref int pos)
            {
                pos++;
                if ((uint)pos >= (uint)format.Length)
                {
                    ThrowHelper.ThrowFormatInvalidString(pos, ExceptionResource.Format_UnclosedFormatItem);
                }
                return format[pos];
            }
        }
    }
    public static class Ascii
    {
        /// <summary>
        /// A mask which selects only the high bit of each byte of the given <see cref="uint"/>.
        /// </summary>
        private const uint UInt32HighBitsOnlyMask = 0x80808080u;
        /// <summary>
        /// Determines whether the provided value is ASCII byte.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>True if <paramref name="value"/> is ASCII, False otherwise.</returns>
        public static bool IsValid(byte value) => value <= 127;

        /// <summary>
        /// Determines whether the provided value is ASCII char.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns>True if <paramref name="value"/> is ASCII, False otherwise.</returns>
        public static bool IsValid(char value) => value <= 127;

        /// <summary>
        /// Copies text from a source buffer to a destination buffer, converting
        /// ASCII letters to uppercase during the copy.
        /// </summary>
        /// <param name="source">The source buffer from which ASCII text is read.</param>
        /// <param name="destination">The destination buffer to which uppercase text is written.</param>
        /// <returns>An <see cref="OperationStatus"/> describing the result of the operation.</returns>
        /// <remarks>In-place conversion is prohibited, please use <see cref="ToUpperInPlace(Span{byte}, out int)"/> for that.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static System.Buffers.OperationStatus ToUpper(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
            => ChangeCase<byte, byte, ToUpperConversion>(source, destination, out bytesWritten);

        /// <summary>
        /// Copies text from a source buffer to a destination buffer, converting
        /// ASCII letters to uppercase during the copy.
        /// </summary>
        /// <param name="source">The source buffer from which ASCII text is read.</param>
        /// <param name="destination">The destination buffer to which uppercase text is written.</param>
        /// <returns>An <see cref="OperationStatus"/> describing the result of the operation.</returns>
        /// <remarks>In-place conversion is prohibited, please use <see cref="ToUpperInPlace(Span{char}, out int)"/> for that.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static System.Buffers.OperationStatus ToUpper(ReadOnlySpan<char> source, Span<char> destination, out int charsWritten)
            => ChangeCase<ushort, ushort, ToUpperConversion>(
                System.Runtime.InteropServices.MemoryMarshal.Cast<char, ushort>(source), 
                System.Runtime.InteropServices.MemoryMarshal.Cast<char, ushort>(destination), out charsWritten);

        /// <summary>
        /// Copies text from a source buffer to a destination buffer, converting
        /// ASCII letters to uppercase during the copy.
        /// </summary>
        /// <param name="source">The source buffer from which ASCII text is read.</param>
        /// <param name="destination">The destination buffer to which uppercase text is written.</param>
        /// <returns>An <see cref="OperationStatus"/> describing the result of the operation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static System.Buffers.OperationStatus ToUpper(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten)
            => ChangeCase<byte, ushort, ToUpperConversion>(source, System.Runtime.InteropServices.MemoryMarshal.Cast<char, ushort>(destination), out charsWritten);

        /// <summary>
        /// Copies text from a source buffer to a destination buffer, converting
        /// ASCII letters to uppercase during the copy.
        /// </summary>
        /// <param name="source">The source buffer from which ASCII text is read.</param>
        /// <param name="destination">The destination buffer to which uppercase text is written.</param>
        /// <returns>An <see cref="OperationStatus"/> describing the result of the operation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static System.Buffers.OperationStatus ToUpper(ReadOnlySpan<char> source, Span<byte> destination, out int bytesWritten)
            => ChangeCase<ushort, byte, ToUpperConversion>(System.Runtime.InteropServices.MemoryMarshal.Cast<char, ushort>(source), destination, out bytesWritten);

        /// <summary>
        /// Copies text from a source buffer to a destination buffer, converting
        /// ASCII letters to lowercase during the copy.
        /// </summary>
        /// <param name="source">The source buffer from which ASCII text is read.</param>
        /// <param name="destination">The destination buffer to which lowercase text is written.</param>
        /// <returns>An <see cref="OperationStatus"/> describing the result of the operation.</returns>
        /// <remarks>In-place conversion is prohibited, please use <see cref="ToLowerInPlace(Span{byte}, out int)"/> for that.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static System.Buffers.OperationStatus ToLower(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
            => ChangeCase<byte, byte, ToLowerConversion>(source, destination, out bytesWritten);

        /// <summary>
        /// Copies text from a source buffer to a destination buffer, converting
        /// ASCII letters to lowercase during the copy.
        /// </summary>
        /// <param name="source">The source buffer from which ASCII text is read.</param>
        /// <param name="destination">The destination buffer to which lowercase text is written.</param>
        /// <returns>An <see cref="OperationStatus"/> describing the result of the operation.</returns>
        /// <remarks>In-place conversion is prohibited, please use <see cref="ToLowerInPlace(Span{char}, out int)"/> for that.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static System.Buffers.OperationStatus ToLower(ReadOnlySpan<char> source, Span<char> destination, out int charsWritten)
            => ChangeCase<ushort, ushort, ToLowerConversion>(
                System.Runtime.InteropServices.MemoryMarshal.Cast<char, ushort>(source), 
                System.Runtime.InteropServices.MemoryMarshal.Cast<char, ushort>(destination), out charsWritten);

        /// <summary>
        /// Copies text from a source buffer to a destination buffer, converting
        /// ASCII letters to lowercase during the copy.
        /// </summary>
        /// <param name="source">The source buffer from which ASCII text is read.</param>
        /// <param name="destination">The destination buffer to which lowercase text is written.</param>
        /// <returns>An <see cref="OperationStatus"/> describing the result of the operation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static System.Buffers.OperationStatus ToLower(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten)
            => ChangeCase<byte, ushort, ToLowerConversion>(source, System.Runtime.InteropServices.MemoryMarshal.Cast<char, ushort>(destination), out charsWritten);

        /// <summary>
        /// Copies text from a source buffer to a destination buffer, converting
        /// ASCII letters to lowercase during the copy.
        /// </summary>
        /// <param name="source">The source buffer from which ASCII text is read.</param>
        /// <param name="destination">The destination buffer to which lowercase text is written.</param>
        /// <returns>An <see cref="OperationStatus"/> describing the result of the operation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static System.Buffers.OperationStatus ToLower(ReadOnlySpan<char> source, Span<byte> destination, out int bytesWritten)
            => ChangeCase<ushort, byte, ToLowerConversion>(System.Runtime.InteropServices.MemoryMarshal.Cast<char, ushort>(source), destination, out bytesWritten);

        /// <summary>
        /// Performs in-place uppercase conversion.
        /// </summary>
        /// <param name="value">The ASCII text buffer.</param>
        /// <param name="bytesWritten">The number of processed bytes.</param>
        /// <returns>An <see cref="OperationStatus"/> describing the result of the operation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static System.Buffers.OperationStatus ToLowerInPlace(Span<byte> value, out int bytesWritten)
            => ChangeCase<byte, ToLowerConversion>(value, out bytesWritten);

        /// <summary>
        /// Performs in-place uppercase conversion.
        /// </summary>
        /// <param name="value">The ASCII text buffer.</param>
        /// <param name="charsWritten">The number of processed characters.</param>
        /// <returns>An <see cref="OperationStatus"/> describing the result of the operation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static System.Buffers.OperationStatus ToLowerInPlace(Span<char> value, out int charsWritten)
            => ChangeCase<ushort, ToLowerConversion>(System.Runtime.InteropServices.MemoryMarshal.Cast<char, ushort>(value), out charsWritten);

        /// <summary>
        /// Performs in-place lowercase conversion.
        /// </summary>
        /// <param name="value">The ASCII text buffer.</param>
        /// <param name="bytesWritten">The number of processed bytes.</param>
        /// <returns>An <see cref="OperationStatus"/> describing the result of the operation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static System.Buffers.OperationStatus ToUpperInPlace(Span<byte> value, out int bytesWritten)
            => ChangeCase<byte, ToUpperConversion>(value, out bytesWritten);

        /// <summary>
        /// Performs in-place lowercase conversion.
        /// </summary>
        /// <param name="value">The ASCII text buffer.</param>
        /// <param name="charsWritten">The number of processed characters.</param>
        /// <returns>An <see cref="OperationStatus"/> describing the result of the operation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static System.Buffers.OperationStatus ToUpperInPlace(Span<char> value, out int charsWritten)
            => ChangeCase<ushort, ToUpperConversion>(System.Runtime.InteropServices.MemoryMarshal.Cast<char, ushort>(value), out charsWritten);

        private static unsafe System.Buffers.OperationStatus ChangeCase<TFrom, TTo, TCasing>(
            ReadOnlySpan<TFrom> source, Span<TTo> destination, out int destinationElementsWritten)
            where TFrom : unmanaged, IBinaryInteger<TFrom>
            where TTo : unmanaged, IBinaryInteger<TTo>
            where TCasing : struct
        {
            if (System.Runtime.InteropServices.MemoryMarshal.AsBytes(source).Overlaps(System.Runtime.InteropServices.MemoryMarshal.AsBytes(destination)))
            {
                throw new InvalidOperationException();
            }

            nuint numElementsToConvert;
            System.Buffers.OperationStatus statusToReturnOnSuccess;

            if (source.Length <= destination.Length)
            {
                numElementsToConvert = (uint)source.Length;
                statusToReturnOnSuccess = System.Buffers.OperationStatus.Done;
            }
            else
            {
                numElementsToConvert = (uint)destination.Length;
                statusToReturnOnSuccess = System.Buffers.OperationStatus.DestinationTooSmall;
            }

            fixed (TFrom* pSource = &System.Runtime.InteropServices.MemoryMarshal.GetReference(source))
            fixed (TTo* pDestination = &System.Runtime.InteropServices.MemoryMarshal.GetReference(destination))
            {
                nuint numElementsActuallyConverted = ChangeCase<TFrom, TTo, TCasing>(pSource, pDestination, numElementsToConvert);
                destinationElementsWritten = (int)numElementsActuallyConverted;
                return (numElementsToConvert == numElementsActuallyConverted) ? statusToReturnOnSuccess : System.Buffers.OperationStatus.InvalidData;
            }
        }

        private static unsafe System.Buffers.OperationStatus ChangeCase<T, TCasing>(Span<T> buffer, out int elementsWritten)
            where T : unmanaged, IBinaryInteger<T>
            where TCasing : struct
        {
            fixed (T* pBuffer = &System.Runtime.InteropServices.MemoryMarshal.GetReference(buffer))
            {
                nuint numElementsActuallyConverted = ChangeCase<T, T, TCasing>(pBuffer, pBuffer, (nuint)buffer.Length);
                elementsWritten = (int)numElementsActuallyConverted;
                return elementsWritten == buffer.Length ? System.Buffers.OperationStatus.Done : System.Buffers.OperationStatus.InvalidData;
            }
        }

        private static unsafe nuint ChangeCase<TFrom, TTo, TCasing>(TFrom* pSrc, TTo* pDest, nuint elementCount)
            where TFrom : unmanaged, IBinaryInteger<TFrom>
            where TTo : unmanaged, IBinaryInteger<TTo>
            where TCasing : struct
        {
            bool sourceIsAscii = (sizeof(TFrom) == 1); // JIT turns this into a const
            bool destIsAscii = (sizeof(TTo) == 1); // JIT turns this into a const
            bool conversionIsWidening = sourceIsAscii && !destIsAscii; // JIT turns this into a const
            bool conversionIsNarrowing = !sourceIsAscii && destIsAscii; // JIT turns this into a const
            bool conversionIsWidthPreserving = typeof(TFrom) == typeof(TTo); // JIT turns this into a const
            bool conversionIsToUpper = (typeof(TCasing) == typeof(ToUpperConversion)); // JIT turns this into a const

            nuint i = 0;

            if (!conversionIsWidthPreserving)
            {
                goto DrainRemaining;
            }

        DrainRemaining:

            // Process single elements at a time.

            for (; i < elementCount; i++)
            {
                uint element = uint.CreateTruncating(pSrc[i]);
                if (!UnicodeUtility.IsAsciiCodePoint(element))
                {
                    break;
                }

                if (conversionIsToUpper)
                {
                    if (UnicodeUtility.IsInRangeInclusive(element, 'a', 'z'))
                    {
                        element -= 0x20u; // lowercase to uppercase
                    }
                }
                else
                {
                    if (UnicodeUtility.IsInRangeInclusive(element, 'A', 'Z'))
                    {
                        element += 0x20u; // uppercase to lowercase
                    }
                }
                pDest[i] = TTo.CreateTruncating(element);
            }

        Return:

            return i;
        }

        private struct ToUpperConversion { }
        private struct ToLowerConversion { }

        /// <summary>
        /// Returns <see langword="true"/> iff all bytes in <paramref name="value"/> are ASCII.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool AllBytesInUInt32AreAscii(uint value)
        {
            // If the high bit of any byte is set, that byte is non-ASCII.

            return (value & UInt32HighBitsOnlyMask) == 0;
        }

        /// <summary>
        /// Returns <see langword="true"/> iff all chars in <paramref name="value"/> are ASCII.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AllCharsInUInt32AreAscii(uint value)
        {
            return (value & ~0x007F007Fu) == 0;
        }

        /// <summary>
        /// Returns <see langword="true"/> iff all chars in <paramref name="value"/> are ASCII.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AllCharsInUInt64AreAscii(ulong value)
        {
            return (value & ~0x007F007F_007F007Ful) == 0;
        }

        /// <summary>
        /// Given a DWORD which represents two packed chars in machine-endian order,
        /// <see langword="true"/> iff the first char (in machine-endian order) is ASCII.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool FirstCharInUInt32IsAscii(uint value)
        {
            return (BitConverter.IsLittleEndian && (value & 0xFF80u) == 0)
                || (!BitConverter.IsLittleEndian && (value & 0xFF800000u) == 0);
        }

        /// <summary>
        /// Copies as many ASCII characters (U+0000..U+007F) as possible from <paramref name="pUtf16Buffer"/>
        /// to <paramref name="pAsciiBuffer"/>, stopping when the first non-ASCII character is encountered
        /// or once <paramref name="elementCount"/> elements have been converted. Returns the total number
        /// of elements that were able to be converted.
        /// </summary>
        internal static unsafe nuint NarrowUtf16ToAscii(char* pUtf16Buffer, byte* pAsciiBuffer, nuint elementCount)
        {
            nuint currentOffset = 0;

            uint utf16Data32BitsHigh = 0, utf16Data32BitsLow = 0;
            ulong utf16Data64Bits = 0;

            if (BitConverter.IsLittleEndian && Vector128.IsHardwareAccelerated && elementCount >= 2 * (uint)Vector128<byte>.Count)
            {
                // Since there's overhead to setting up the vectorized code path, we only want to
                // call into it after a quick probe to ensure the next immediate characters really are ASCII.
                // If we see non-ASCII data, we'll jump immediately to the draining logic at the end of the method.

                if (IntPtr.Size >= 8)
                {
                    utf16Data64Bits = Unsafe.ReadUnaligned<ulong>(pUtf16Buffer);
                    if (!AllCharsInUInt64AreAscii(utf16Data64Bits))
                    {
                        goto FoundNonAsciiDataIn64BitRead;
                    }
                }
                else
                {
                    utf16Data32BitsHigh = Unsafe.ReadUnaligned<uint>(pUtf16Buffer);
                    utf16Data32BitsLow = Unsafe.ReadUnaligned<uint>(pUtf16Buffer + 4 / sizeof(char));
                    if (!AllCharsInUInt32AreAscii(utf16Data32BitsHigh | utf16Data32BitsLow))
                    {
                        goto FoundNonAsciiDataIn64BitRead;
                    }
                }
                if (Vector512.IsHardwareAccelerated && elementCount >= 2 * (uint)Vector512<byte>.Count)
                {
                    currentOffset = NarrowUtf16ToAscii_Intrinsified_512(pUtf16Buffer, pAsciiBuffer, elementCount);
                }
                else if (Vector256.IsHardwareAccelerated && elementCount >= 2 * (uint)Vector256<byte>.Count)
                {
                    currentOffset = NarrowUtf16ToAscii_Intrinsified_256(pUtf16Buffer, pAsciiBuffer, elementCount);
                }
                else
                {
                    currentOffset = NarrowUtf16ToAscii_Intrinsified(pUtf16Buffer, pAsciiBuffer, elementCount);
                }
            }

            nuint remainingElementCount = elementCount - currentOffset;

            // Try to narrow 64 bits -> 32 bits at a time.
            // We needn't update remainingElementCount after this point.

            if (remainingElementCount >= 4)
            {
                nuint finalOffsetWhereCanLoop = currentOffset + remainingElementCount - 4;
                do
                {
                    if (IntPtr.Size >= 8)
                    {
                        // Only perform QWORD reads on a 64-bit platform.
                        utf16Data64Bits = Unsafe.ReadUnaligned<ulong>(pUtf16Buffer + currentOffset);
                        if (!AllCharsInUInt64AreAscii(utf16Data64Bits))
                        {
                            goto FoundNonAsciiDataIn64BitRead;
                        }

                        NarrowFourUtf16CharsToAsciiAndWriteToBuffer(ref pAsciiBuffer[currentOffset], utf16Data64Bits);
                    }
                    else
                    {
                        utf16Data32BitsHigh = Unsafe.ReadUnaligned<uint>(pUtf16Buffer + currentOffset);
                        utf16Data32BitsLow = Unsafe.ReadUnaligned<uint>(pUtf16Buffer + currentOffset + 4 / sizeof(char));
                        if (!AllCharsInUInt32AreAscii(utf16Data32BitsHigh | utf16Data32BitsLow))
                        {
                            goto FoundNonAsciiDataIn64BitRead;
                        }

                        NarrowTwoUtf16CharsToAsciiAndWriteToBuffer(ref pAsciiBuffer[currentOffset], utf16Data32BitsHigh);
                        NarrowTwoUtf16CharsToAsciiAndWriteToBuffer(ref pAsciiBuffer[currentOffset + 2], utf16Data32BitsLow);
                    }

                    currentOffset += 4;
                } while (currentOffset <= finalOffsetWhereCanLoop);
            }

            // Try to narrow 32 bits -> 16 bits.

            if (((uint)remainingElementCount & 2) != 0)
            {
                utf16Data32BitsHigh = Unsafe.ReadUnaligned<uint>(pUtf16Buffer + currentOffset);
                if (!AllCharsInUInt32AreAscii(utf16Data32BitsHigh))
                {
                    goto FoundNonAsciiDataInHigh32Bits;
                }

                NarrowTwoUtf16CharsToAsciiAndWriteToBuffer(ref pAsciiBuffer[currentOffset], utf16Data32BitsHigh);
                currentOffset += 2;
            }

            // Try to narrow 16 bits -> 8 bits.

            if (((uint)remainingElementCount & 1) != 0)
            {
                utf16Data32BitsHigh = pUtf16Buffer[currentOffset];
                if (utf16Data32BitsHigh <= 0x007Fu)
                {
                    pAsciiBuffer[currentOffset] = (byte)utf16Data32BitsHigh;
                    currentOffset++;
                }
            }

        Finish:

            return currentOffset;

        FoundNonAsciiDataIn64BitRead:

            if (IntPtr.Size >= 8)
            {
                // Try checking the first 32 bits of the buffer for non-ASCII data.
                // Regardless, we'll move the non-ASCII data into the utf16Data32BitsHigh local.

                if (BitConverter.IsLittleEndian)
                {
                    utf16Data32BitsHigh = (uint)utf16Data64Bits;
                }
                else
                {
                    utf16Data32BitsHigh = (uint)(utf16Data64Bits >> 32);
                }

                if (AllCharsInUInt32AreAscii(utf16Data32BitsHigh))
                {
                    NarrowTwoUtf16CharsToAsciiAndWriteToBuffer(ref pAsciiBuffer[currentOffset], utf16Data32BitsHigh);

                    if (BitConverter.IsLittleEndian)
                    {
                        utf16Data32BitsHigh = (uint)(utf16Data64Bits >> 32);
                    }
                    else
                    {
                        utf16Data32BitsHigh = (uint)utf16Data64Bits;
                    }

                    currentOffset += 2;
                }
            }
            else
            {
                // Need to determine if the high or the low 32-bit value contained non-ASCII data.
                // Regardless, we'll move the non-ASCII data into the utf16Data32BitsHigh local.

                if (AllCharsInUInt32AreAscii(utf16Data32BitsHigh))
                {
                    NarrowTwoUtf16CharsToAsciiAndWriteToBuffer(ref pAsciiBuffer[currentOffset], utf16Data32BitsHigh);
                    utf16Data32BitsHigh = utf16Data32BitsLow;
                    currentOffset += 2;
                }
            }

        FoundNonAsciiDataInHigh32Bits:

            // There's at most one char that needs to be drained.

            if (FirstCharInUInt32IsAscii(utf16Data32BitsHigh))
            {
                if (!BitConverter.IsLittleEndian)
                {
                    utf16Data32BitsHigh >>= 16; // move high char down to low char
                }

                pAsciiBuffer[currentOffset] = (byte)utf16Data32BitsHigh;
                currentOffset++;
            }

            goto Finish;
        }

        /// <summary>
        /// Copies as many ASCII bytes (00..7F) as possible from <paramref name="pAsciiBuffer"/>
        /// to <paramref name="pUtf16Buffer"/>, stopping when the first non-ASCII byte is encountered
        /// or once <paramref name="elementCount"/> elements have been converted. Returns the total number
        /// of elements that were able to be converted.
        /// </summary>
        internal static unsafe nuint WidenAsciiToUtf16(byte* pAsciiBuffer, char* pUtf16Buffer, nuint elementCount)
        {
            // Intrinsified in mono interpreter
            nuint currentOffset = 0;

            if (BitConverter.IsLittleEndian && Vector128.IsHardwareAccelerated && elementCount >= (uint)Vector128<byte>.Count)
            {
                if (Vector512.IsHardwareAccelerated && (elementCount - currentOffset) >= (uint)Vector512<byte>.Count)
                {
                    WidenAsciiToUtf1_Vector<Vector512<byte>, Vector512<ushort>>(pAsciiBuffer, pUtf16Buffer, ref currentOffset, elementCount);
                }
                else if (Vector256.IsHardwareAccelerated && (elementCount - currentOffset) >= (uint)Vector256<byte>.Count)
                {
                    WidenAsciiToUtf1_Vector<Vector256<byte>, Vector256<ushort>>(pAsciiBuffer, pUtf16Buffer, ref currentOffset, elementCount);
                }
                else if (Vector128.IsHardwareAccelerated && (elementCount - currentOffset) >= (uint)Vector128<byte>.Count)
                {
                    WidenAsciiToUtf1_Vector<Vector128<byte>, Vector128<ushort>>(pAsciiBuffer, pUtf16Buffer, ref currentOffset, elementCount);
                }
            }

            nuint remainingElementCount = elementCount - currentOffset;

            // Try to widen 32 bits -> 64 bits at a time.
            // We needn't update remainingElementCount after this point.

            uint asciiData;

            if (remainingElementCount >= 4)
            {
                nuint finalOffsetWhereCanLoop = currentOffset + remainingElementCount - 4;
                do
                {
                    asciiData = Unsafe.ReadUnaligned<uint>(pAsciiBuffer + currentOffset);
                    if (!AllBytesInUInt32AreAscii(asciiData))
                    {
                        goto FoundNonAsciiData;
                    }

                    WidenFourAsciiBytesToUtf16AndWriteToBuffer(ref pUtf16Buffer[currentOffset], asciiData);
                    currentOffset += 4;
                } while (currentOffset <= finalOffsetWhereCanLoop);
            }

            // Try to widen 16 bits -> 32 bits.

            if (((uint)remainingElementCount & 2) != 0)
            {
                asciiData = Unsafe.ReadUnaligned<ushort>(pAsciiBuffer + currentOffset);
                if (!AllBytesInUInt32AreAscii(asciiData))
                {
                    if (!BitConverter.IsLittleEndian)
                    {
                        asciiData <<= 16;
                    }
                    goto FoundNonAsciiData;
                }

                if (BitConverter.IsLittleEndian)
                {
                    pUtf16Buffer[currentOffset] = (char)(byte)asciiData;
                    pUtf16Buffer[currentOffset + 1] = (char)(asciiData >> 8);
                }
                else
                {
                    pUtf16Buffer[currentOffset + 1] = (char)(byte)asciiData;
                    pUtf16Buffer[currentOffset] = (char)(asciiData >> 8);
                }

                currentOffset += 2;
            }

            // Try to widen 8 bits -> 16 bits.

            if (((uint)remainingElementCount & 1) != 0)
            {
                asciiData = pAsciiBuffer[currentOffset];
                if (((byte)asciiData & 0x80) != 0)
                {
                    goto Finish;
                }

                pUtf16Buffer[currentOffset] = (char)asciiData;
                currentOffset++;
            }

        Finish:

            return currentOffset;

        FoundNonAsciiData:

            // Drain ASCII bytes one at a time.

            if (BitConverter.IsLittleEndian)
            {
                while (((byte)asciiData & 0x80) == 0)
                {
                    pUtf16Buffer[currentOffset] = (char)(byte)asciiData;
                    currentOffset++;
                    asciiData >>= 8;
                }
            }
            else
            {
                while ((asciiData & 0x80000000) == 0)
                {
                    asciiData = BitOperations.RotateLeft(asciiData, 8);
                    pUtf16Buffer[currentOffset] = (char)(byte)asciiData;
                    currentOffset++;
                }
            }

            goto Finish;
        }

        /// <summary>
        /// Given a QWORD which represents a buffer of 4 ASCII chars in machine-endian order,
        /// narrows each WORD to a BYTE, then writes the 4-byte result to the output buffer
        /// also in machine-endian order.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void NarrowFourUtf16CharsToAsciiAndWriteToBuffer(ref byte outputBuffer, ulong value)
        {
            if (Sse2.X64.IsSupported)
            {
                // Narrows a vector of words [ w0 w1 w2 w3 ] to a vector of bytes
                // [ b0 b1 b2 b3 b0 b1 b2 b3 ], then writes 4 bytes (32 bits) to the destination.

                Vector128<short> vecWide = Sse2.X64.ConvertScalarToVector128UInt64(value).AsInt16();
                Vector128<uint> vecNarrow = Sse2.PackUnsignedSaturate(vecWide, vecWide).AsUInt32();
                Unsafe.WriteUnaligned(ref outputBuffer, Sse2.ConvertToUInt32(vecNarrow));
            }
            else if (AdvSimd.IsSupported)
            {
                // Narrows a vector of words [ w0 w1 w2 w3 ] to a vector of bytes
                // [ b0 b1 b2 b3 * * * * ], then writes 4 bytes (32 bits) to the destination.

                Vector128<short> vecWide = Vector128.CreateScalarUnsafe(value).AsInt16();
                Vector64<byte> lower = AdvSimd.ExtractNarrowingSaturateUnsignedLower(vecWide);
                Unsafe.WriteUnaligned(ref outputBuffer, lower.AsUInt32().ToScalar());
            }
            else
            {
                if (BitConverter.IsLittleEndian)
                {
                    outputBuffer = (byte)value;
                    value >>= 16;
                    Unsafe.Add(ref outputBuffer, 1) = (byte)value;
                    value >>= 16;
                    Unsafe.Add(ref outputBuffer, 2) = (byte)value;
                    value >>= 16;
                    Unsafe.Add(ref outputBuffer, 3) = (byte)value;
                }
                else
                {
                    Unsafe.Add(ref outputBuffer, 3) = (byte)value;
                    value >>= 16;
                    Unsafe.Add(ref outputBuffer, 2) = (byte)value;
                    value >>= 16;
                    Unsafe.Add(ref outputBuffer, 1) = (byte)value;
                    value >>= 16;
                    outputBuffer = (byte)value;
                }
            }
        }

        /// <summary>
        /// Given a DWORD which represents a buffer of 2 ASCII chars in machine-endian order,
        /// narrows each WORD to a BYTE, then writes the 2-byte result to the output buffer also in
        /// machine-endian order.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void NarrowTwoUtf16CharsToAsciiAndWriteToBuffer(ref byte outputBuffer, uint value)
        {
            if (BitConverter.IsLittleEndian)
            {
                outputBuffer = (byte)value;
                Unsafe.Add(ref outputBuffer, 1) = (byte)(value >> 16);
            }
            else
            {
                Unsafe.Add(ref outputBuffer, 1) = (byte)value;
                outputBuffer = (byte)(value >> 16);
            }
        }

        /// <summary>
        /// Given a DWORD which represents a buffer of 4 bytes, widens the buffer into 4 WORDs and
        /// writes them to the output buffer with machine endianness.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WidenFourAsciiBytesToUtf16AndWriteToBuffer(ref char outputBuffer, uint value)
        {
            if (AdvSimd.Arm64.IsSupported)
            {
                Vector128<byte> vecNarrow = AdvSimd.DuplicateToVector128(value).AsByte();
                Vector128<ulong> vecWide = AdvSimd.Arm64.ZipLow(vecNarrow, Vector128<byte>.Zero).AsUInt64();
                Unsafe.WriteUnaligned(ref Unsafe.As<char, byte>(ref outputBuffer), vecWide.ToScalar());
            }
            else if (Vector128.IsHardwareAccelerated)
            {
                Vector128<byte> vecNarrow = Vector128.CreateScalar(value).AsByte();
                Vector128<ulong> vecWide = Vector128.WidenLower(vecNarrow).AsUInt64();
                Unsafe.WriteUnaligned(ref Unsafe.As<char, byte>(ref outputBuffer), vecWide.ToScalar());
            }
            else
            {
                if (BitConverter.IsLittleEndian)
                {
                    outputBuffer = (char)(byte)value;
                    value >>= 8;
                    Unsafe.Add(ref outputBuffer, 1) = (char)(byte)value;
                    value >>= 8;
                    Unsafe.Add(ref outputBuffer, 2) = (char)(byte)value;
                    value >>= 8;
                    Unsafe.Add(ref outputBuffer, 3) = (char)value;
                }
                else
                {
                    Unsafe.Add(ref outputBuffer, 3) = (char)(byte)value;
                    value >>= 8;
                    Unsafe.Add(ref outputBuffer, 2) = (char)(byte)value;
                    value >>= 8;
                    Unsafe.Add(ref outputBuffer, 1) = (char)(byte)value;
                    value >>= 8;
                    outputBuffer = (char)value;
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<byte> ExtractAsciiVector(Vector128<ushort> vectorFirst, Vector128<ushort> vectorSecond)
        {
            // Narrows two vectors of words [ w7 w6 w5 w4 w3 w2 w1 w0 ] and [ w7' w6' w5' w4' w3' w2' w1' w0' ]
            // to a vector of bytes [ b7 ... b0 b7' ... b0'].

            // prefer architecture specific intrinsic as they don't perform additional AND like Vector128.Narrow does
            if (Sse2.IsSupported)
            {
                return Sse2.PackUnsignedSaturate(vectorFirst.AsInt16(), vectorSecond.AsInt16());
            }
            else if (AdvSimd.Arm64.IsSupported)
            {
                return AdvSimd.Arm64.UnzipEven(vectorFirst.AsByte(), vectorSecond.AsByte());
            }
            else if (PackedSimd.IsSupported)
            {
                return PackedSimd.ConvertNarrowingSaturateUnsigned(vectorFirst.AsInt16(), vectorSecond.AsInt16());
            }
            else
            {
                return Vector128.Narrow(vectorFirst, vectorSecond);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<byte> ExtractAsciiVector(Vector256<ushort> vectorFirst, Vector256<ushort> vectorSecond)
        {
            return Avx2.IsSupported
                ? PackedSpanHelpers.FixUpPackedVector256Result(Avx2.PackUnsignedSaturate(vectorFirst.AsInt16(), vectorSecond.AsInt16()))
                : Vector256.Narrow(vectorFirst, vectorSecond);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<byte> ExtractAsciiVector(Vector512<ushort> vectorFirst, Vector512<ushort> vectorSecond)
        {
            return Avx512BW.IsSupported
                ? PackedSpanHelpers.FixUpPackedVector512Result(Avx512BW.PackUnsignedSaturate(vectorFirst.AsInt16(), vectorSecond.AsInt16()))
                : Vector512.Narrow(vectorFirst, vectorSecond);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasMatch<TVectorByte>(TVectorByte vector)
            where TVectorByte : unmanaged, ISimdVector<TVectorByte, byte>
        {
            return !(vector & TVectorByte.Create((byte)0x80)).Equals(TVectorByte.Zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe nuint NarrowUtf16ToAscii_Intrinsified(char* pUtf16Buffer, byte* pAsciiBuffer, nuint elementCount)
        {
            // This method contains logic optimized using vector instructions for both x64 and Arm64.
            // Much of the logic in this method will be elided by JIT once we determine which specific ISAs we support.

            // JIT turns the below into constants

            uint SizeOfVector128 = (uint)Vector128<byte>.Count;
            nuint MaskOfAllBitsInVector128 = (nuint)(SizeOfVector128 - 1);

            // This method is written such that control generally flows top-to-bottom, avoiding
            // jumps as much as possible in the optimistic case of "all ASCII". If we see non-ASCII
            // data, we jump out of the hot paths to targets at the end of the method.

            // First, perform an unaligned read of the first part of the input buffer.
            ref ushort utf16Buffer = ref *(ushort*)pUtf16Buffer;
            Vector128<ushort> utf16VectorFirst = Vector128.LoadUnsafe(ref utf16Buffer);

            // If there's non-ASCII data in the first 8 elements of the vector, there's nothing we can do.
            if (VectorContainsNonAsciiChar(utf16VectorFirst))
            {
                return 0;
            }

            // Turn the 8 ASCII chars we just read into 8 ASCII bytes, then copy it to the destination.

            ref byte asciiBuffer = ref *pAsciiBuffer;
            Vector128<byte> asciiVector = ExtractAsciiVector(utf16VectorFirst, utf16VectorFirst);
            asciiVector.StoreLowerUnsafe(ref asciiBuffer, 0);
            nuint currentOffsetInElements = SizeOfVector128 / 2; // we processed 8 elements so far

            // We're going to get the best performance when we have aligned writes, so we'll take the
            // hit of potentially unaligned reads in order to hit this sweet spot.

            // pAsciiBuffer points to the start of the destination buffer, immediately before where we wrote
            // the 8 bytes previously. If the 0x08 bit is set at the pinned address, then the 8 bytes we wrote
            // previously mean that the 0x08 bit is *not* set at address &pAsciiBuffer[SizeOfVector128 / 2]. In
            // that case we can immediately back up to the previous aligned boundary and start the main loop.
            // If the 0x08 bit is *not* set at the pinned address, then it means the 0x08 bit *is* set at
            // address &pAsciiBuffer[SizeOfVector128 / 2], and we should perform one more 8-byte write to bump
            // just past the next aligned boundary address.

            if (((uint)pAsciiBuffer & (SizeOfVector128 / 2)) == 0)
            {
                // We need to perform one more partial vector write before we can get the alignment we want.

                utf16VectorFirst = Vector128.LoadUnsafe(ref utf16Buffer, currentOffsetInElements);

                if (VectorContainsNonAsciiChar(utf16VectorFirst))
                {
                    goto Finish;
                }

                // Turn the 8 ASCII chars we just read into 8 ASCII bytes, then copy it to the destination.
                asciiVector = ExtractAsciiVector(utf16VectorFirst, utf16VectorFirst);
                asciiVector.StoreLowerUnsafe(ref asciiBuffer, currentOffsetInElements);
            }

            // Calculate how many elements we wrote in order to get pAsciiBuffer to its next alignment
            // point, then use that as the base offset going forward.

            currentOffsetInElements = SizeOfVector128 - ((nuint)pAsciiBuffer & MaskOfAllBitsInVector128);

            nuint finalOffsetWhereCanRunLoop = elementCount - SizeOfVector128;
            do
            {
                // In a loop, perform two unaligned reads, narrow to a single vector, then aligned write one vector.

                utf16VectorFirst = Vector128.LoadUnsafe(ref utf16Buffer, currentOffsetInElements);
                Vector128<ushort> utf16VectorSecond = Vector128.LoadUnsafe(ref utf16Buffer, currentOffsetInElements + SizeOfVector128 / sizeof(short));
                Vector128<ushort> combinedVector = utf16VectorFirst | utf16VectorSecond;

                if (VectorContainsNonAsciiChar(combinedVector))
                {
                    goto FoundNonAsciiDataInLoop;
                }

                // Build up the ASCII vector and perform the store.

                asciiVector = ExtractAsciiVector(utf16VectorFirst, utf16VectorSecond);
                asciiVector.StoreUnsafe(ref asciiBuffer, currentOffsetInElements);

                currentOffsetInElements += SizeOfVector128;
            } while (currentOffsetInElements <= finalOffsetWhereCanRunLoop);

        Finish:

            // There might be some ASCII data left over. That's fine - we'll let our caller handle the final drain.
            return currentOffsetInElements;

        FoundNonAsciiDataInLoop:

            // Can we at least narrow the high vector?
            // See comments in GetIndexOfFirstNonAsciiChar_Intrinsified for information about how this works.
            if (VectorContainsNonAsciiChar(utf16VectorFirst))
            {
                goto Finish;
            }

            // First part was all ASCII, narrow and aligned write. Note we're only filling in the low half of the vector.

            asciiVector = ExtractAsciiVector(utf16VectorFirst, utf16VectorFirst);
            asciiVector.StoreLowerUnsafe(ref asciiBuffer, currentOffsetInElements);
            currentOffsetInElements += SizeOfVector128 / 2;

            goto Finish;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe nuint NarrowUtf16ToAscii_Intrinsified_256(char* pUtf16Buffer, byte* pAsciiBuffer, nuint elementCount)
        {
            // This method contains logic optimized using vector instructions for x64 only.
            // Much of the logic in this method will be elided by JIT once we determine which specific ISAs we support.

            // JIT turns the below into constants

            const nuint MaskOfAllBitsInVector256 = (nuint)(Vector256.Size - 1);

            // This method is written such that control generally flows top-to-bottom, avoiding
            // jumps as much as possible in the optimistic case of "all ASCII". If we see non-ASCII
            // data, we jump out of the hot paths to targets at the end of the method.

            // First, perform an unaligned read of the first part of the input buffer.
            ref ushort utf16Buffer = ref *(ushort*)pUtf16Buffer;
            Vector256<ushort> utf16VectorFirst = Vector256.LoadUnsafe(ref utf16Buffer);

            // If there's non-ASCII data in the first 16 elements of the vector, there's nothing we can do.
            if (VectorContainsNonAsciiChar(utf16VectorFirst))
            {
                return 0;
            }

            // Turn the 16 ASCII chars we just read into 16 ASCII bytes, then copy it to the destination.

            ref byte asciiBuffer = ref *pAsciiBuffer;
            Vector256<byte> asciiVector = ExtractAsciiVector(utf16VectorFirst, utf16VectorFirst);
            asciiVector.GetLower().StoreUnsafe(ref asciiBuffer, 0);
            nuint currentOffsetInElements = Vector256.Size / 2; // we processed 16 elements so far

            // We're going to get the best performance when we have aligned writes, so we'll take the
            // hit of potentially unaligned reads in order to hit this sweet spot.

            // pAsciiBuffer points to the start of the destination buffer, immediately before where we wrote
            // the 16 bytes previously. If the 0x10 bit is set at the pinned address, then the 16 bytes we wrote
            // previously mean that the 0x10 bit is *not* set at address &pAsciiBuffer[SizeOfVector256 / 2]. In
            // that case we can immediately back up to the previous aligned boundary and start the main loop.
            // If the 0x10 bit is *not* set at the pinned address, then it means the 0x10 bit *is* set at
            // address &pAsciiBuffer[SizeOfVector256 / 2], and we should perform one more 16-byte write to bump
            // just past the next aligned boundary address.
            if (((uint)pAsciiBuffer & (Vector256.Size / 2)) == 0)
            {
                // We need to perform one more partial vector write before we can get the alignment we want.

                utf16VectorFirst = Vector256.LoadUnsafe(ref utf16Buffer, currentOffsetInElements);

                if (VectorContainsNonAsciiChar(utf16VectorFirst))
                {
                    goto Finish;
                }

                // Turn the 16 ASCII chars we just read into 16 ASCII bytes, then copy it to the destination.
                asciiVector = ExtractAsciiVector(utf16VectorFirst, utf16VectorFirst);
                asciiVector.GetLower().StoreUnsafe(ref asciiBuffer, currentOffsetInElements);
            }

            // Calculate how many elements we wrote in order to get pAsciiBuffer to its next alignment
            // point, then use that as the base offset going forward.

            currentOffsetInElements = Vector256.Size - ((nuint)pAsciiBuffer & MaskOfAllBitsInVector256);

            nuint finalOffsetWhereCanRunLoop = elementCount - Vector256.Size;
            do
            {
                // In a loop, perform two unaligned reads, narrow to a single vector, then aligned write one vector.

                utf16VectorFirst = Vector256.LoadUnsafe(ref utf16Buffer, currentOffsetInElements);
                Vector256<ushort> utf16VectorSecond = Vector256.LoadUnsafe(ref utf16Buffer, currentOffsetInElements + Vector256.Size / sizeof(short));
                Vector256<ushort> combinedVector = utf16VectorFirst | utf16VectorSecond;

                if (VectorContainsNonAsciiChar(combinedVector))
                {
                    goto FoundNonAsciiDataInLoop;
                }

                // Build up the ASCII vector and perform the store.

                asciiVector = ExtractAsciiVector(utf16VectorFirst, utf16VectorSecond);
                asciiVector.StoreUnsafe(ref asciiBuffer, currentOffsetInElements);

                currentOffsetInElements += Vector256.Size;
            } while (currentOffsetInElements <= finalOffsetWhereCanRunLoop);

        Finish:

            // There might be some ASCII data left over. That's fine - we'll let our caller handle the final drain.
            return currentOffsetInElements;

        FoundNonAsciiDataInLoop:

            // Can we at least narrow the high vector?
            // See comments in GetIndexOfFirstNonAsciiChar_Intrinsified for information about how this works.
            if (VectorContainsNonAsciiChar(utf16VectorFirst))
            {
                goto Finish;
            }

            // First part was all ASCII, narrow and aligned write. Note we're only filling in the low half of the vector.

            asciiVector = ExtractAsciiVector(utf16VectorFirst, utf16VectorFirst);
            asciiVector.GetLower().StoreUnsafe(ref asciiBuffer, currentOffsetInElements);
            currentOffsetInElements += Vector256.Size / 2;

            goto Finish;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe nuint NarrowUtf16ToAscii_Intrinsified_512(char* pUtf16Buffer, byte* pAsciiBuffer, nuint elementCount)
        {
            // This method contains logic optimized using vector instructions for x64 only.
            // Much of the logic in this method will be elided by JIT once we determine which specific ISAs we support.

            // JIT turns the below into constants

            const nuint MaskOfAllBitsInVector512 = (nuint)(Vector512.Size - 1);

            // This method is written such that control generally flows top-to-bottom, avoiding
            // jumps as much as possible in the optimistic case of "all ASCII". If we see non-ASCII
            // data, we jump out of the hot paths to targets at the end of the method.

            // First, perform an unaligned read of the first part of the input buffer.
            ref ushort utf16Buffer = ref *(ushort*)pUtf16Buffer;
            Vector512<ushort> utf16VectorFirst = Vector512.LoadUnsafe(ref utf16Buffer);

            // If there's non-ASCII data in the first 32 elements of the vector, there's nothing we can do.
            if (VectorContainsNonAsciiChar(utf16VectorFirst))
            {
                return 0;
            }

            // Turn the 32 ASCII chars we just read into 32 ASCII bytes, then copy it to the destination.

            ref byte asciiBuffer = ref *pAsciiBuffer;
            Vector512<byte> asciiVector = ExtractAsciiVector(utf16VectorFirst, utf16VectorFirst);
            asciiVector.GetLower().StoreUnsafe(ref asciiBuffer, 0); // how to store the lower part of a avx512
            nuint currentOffsetInElements = Vector512.Size / 2; // we processed 32 elements so far

            // We're going to get the best performance when we have aligned writes, so we'll take the
            // hit of potentially unaligned reads in order to hit this sweet spot.

            // pAsciiBuffer points to the start of the destination buffer, immediately before where we wrote
            // the 32 bytes previously. If the 0x20 bit is set at the pinned address, then the 32 bytes we wrote
            // previously mean that the 0x20 bit is *not* set at address &pAsciiBuffer[SizeOfVector512 / 2]. In
            // that case we can immediately back up to the previous aligned boundary and start the main loop.
            // If the 0x20 bit is *not* set at the pinned address, then it means the 0x20 bit *is* set at
            // address &pAsciiBuffer[SizeOfVector512 / 2], and we should perform one more 32-byte write to bump
            // just past the next aligned boundary address.

            if (((uint)pAsciiBuffer & (Vector512.Size / 2)) == 0)
            {
                // We need to perform one more partial vector write before we can get the alignment we want.

                utf16VectorFirst = Vector512.LoadUnsafe(ref utf16Buffer, currentOffsetInElements);

                if (VectorContainsNonAsciiChar(utf16VectorFirst))
                {
                    goto Finish;
                }

                // Turn the 32 ASCII chars we just read into 32 ASCII bytes, then copy it to the destination.
                asciiVector = ExtractAsciiVector(utf16VectorFirst, utf16VectorFirst);
                asciiVector.GetLower().StoreUnsafe(ref asciiBuffer, currentOffsetInElements);
            }

            // Calculate how many elements we wrote in order to get pAsciiBuffer to its next alignment
            // point, then use that as the base offset going forward.

            currentOffsetInElements = Vector512.Size - ((nuint)pAsciiBuffer & MaskOfAllBitsInVector512);

            nuint finalOffsetWhereCanRunLoop = elementCount - Vector512.Size;
            do
            {
                // In a loop, perform two unaligned reads, narrow to a single vector, then aligned write one vector.

                utf16VectorFirst = Vector512.LoadUnsafe(ref utf16Buffer, currentOffsetInElements);
                Vector512<ushort> utf16VectorSecond = Vector512.LoadUnsafe(ref utf16Buffer, currentOffsetInElements + Vector512.Size / sizeof(short));
                Vector512<ushort> combinedVector = utf16VectorFirst | utf16VectorSecond;

                if (VectorContainsNonAsciiChar(combinedVector))
                {
                    goto FoundNonAsciiDataInLoop;
                }

                // Build up the ASCII vector and perform the store.

                asciiVector = ExtractAsciiVector(utf16VectorFirst, utf16VectorSecond);
                asciiVector.StoreUnsafe(ref asciiBuffer, currentOffsetInElements);

                currentOffsetInElements += Vector512.Size;
            } while (currentOffsetInElements <= finalOffsetWhereCanRunLoop);

        Finish:

            // There might be some ASCII data left over. That's fine - we'll let our caller handle the final drain.
            return currentOffsetInElements;

        FoundNonAsciiDataInLoop:

            // Can we at least narrow the high vector?
            // See comments in GetIndexOfFirstNonAsciiChar_Intrinsified for information about how this works.
            if (VectorContainsNonAsciiChar(utf16VectorFirst))
            {
                goto Finish;
            }

            // First part was all ASCII, narrow and aligned write. Note we're only filling in the low half of the vector.

            asciiVector = ExtractAsciiVector(utf16VectorFirst, utf16VectorFirst);
            asciiVector.GetLower().StoreUnsafe(ref asciiBuffer, currentOffsetInElements);
            currentOffsetInElements += Vector512.Size / 2;

            goto Finish;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool VectorContainsNonAsciiChar(Vector128<byte> asciiVector)
        {
            // max ASCII character is 0b_0111_1111, so the most significant bit (0x80) tells whether it contains non ascii

            // For performance, prefer architecture specific implementation
            if (Sse41.IsSupported)
            {
                return (asciiVector & Vector128.Create((byte)0x80)) != Vector128<byte>.Zero;
            }
            else if (AdvSimd.Arm64.IsSupported)
            {
                Vector128<byte> maxBytes = AdvSimd.Arm64.MaxPairwise(asciiVector, asciiVector);
                return (maxBytes.AsUInt64().ToScalar() & 0x8080808080808080) != 0;
            }
            else
            {
                return asciiVector.ExtractMostSignificantBits() != 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool VectorContainsNonAsciiChar(Vector128<ushort> utf16Vector)
        {
            // For performance, prefer architecture specific implementation
            if (Sse41.IsSupported)
            {
                const ushort asciiMask = ushort.MaxValue - 127; // 0xFF80
                Vector128<ushort> zeroIsAscii = utf16Vector & Vector128.Create(asciiMask);
                // If a non-ASCII bit is set in any WORD of the vector, we have seen non-ASCII data.
                return zeroIsAscii != Vector128<ushort>.Zero;
            }
            else if (Sse2.IsSupported)
            {
                Vector128<ushort> asciiMaskForAddSaturate = Vector128.Create((ushort)0x7F80);
                // The operation below forces the 0x8000 bit of each WORD to be set iff the WORD element
                // has value >= 0x0800 (non-ASCII). Then we'll treat the vector as a BYTE vector in order
                // to extract the mask. Reminder: the 0x0080 bit of each WORD should be ignored.
                return (Sse2.MoveMask(Sse2.AddSaturate(utf16Vector, asciiMaskForAddSaturate).AsByte()) & 0b_1010_1010_1010_1010) != 0;
            }
            else if (AdvSimd.Arm64.IsSupported)
            {
                // First we pick four chars, a larger one from all four pairs of adjecent chars in the vector.
                // If any of those four chars has a non-ASCII bit set, we have seen non-ASCII data.
                Vector128<ushort> maxChars = AdvSimd.Arm64.MaxPairwise(utf16Vector, utf16Vector);
                return (maxChars.AsUInt64().ToScalar() & 0xFF80FF80FF80FF80) != 0;
            }
            else
            {
                const ushort asciiMask = ushort.MaxValue - 127; // 0xFF80
                Vector128<ushort> zeroIsAscii = utf16Vector & Vector128.Create(asciiMask);
                // If a non-ASCII bit is set in any WORD of the vector, we have seen non-ASCII data.
                return zeroIsAscii != Vector128<ushort>.Zero;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool VectorContainsNonAsciiChar(Vector256<ushort> utf16Vector)
        {
            const ushort asciiMask = ushort.MaxValue - 127; // 0xFF80
            Vector256<ushort> zeroIsAscii = utf16Vector & Vector256.Create(asciiMask);
            // If a non-ASCII bit is set in any WORD of the vector, we have seen non-ASCII data.
            return zeroIsAscii != Vector256<ushort>.Zero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool VectorContainsNonAsciiChar(Vector512<ushort> utf16Vector)
        {
            const ushort asciiMask = ushort.MaxValue - 127; // 0xFF80
            Vector512<ushort> zeroIsAscii = utf16Vector & Vector512.Create(asciiMask);
            // If a non-ASCII bit is set in any WORD of the vector, we have seen non-ASCII data.
            return zeroIsAscii != Vector512<ushort>.Zero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool VectorContainsNonAsciiChar<T>(Vector128<T> vector)
            where T : unmanaged
        {
            return typeof(T) == typeof(byte)
                ? VectorContainsNonAsciiChar(vector.AsByte())
                : VectorContainsNonAsciiChar(vector.AsUInt16());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (TVectorUInt16 Lower, TVectorUInt16 Upper) Widen<TVectorByte, TVectorUInt16>(TVectorByte vector)
            where TVectorByte : unmanaged, ISimdVector<TVectorByte, byte>
            where TVectorUInt16 : unmanaged, ISimdVector<TVectorUInt16, ushort>
        {
            if (typeof(TVectorByte) == typeof(Vector256<byte>))
            {
                (Vector256<ushort> Lower256, Vector256<ushort> Upper256) = Vector256.Widen((Vector256<byte>)(object)vector);
                return ((TVectorUInt16)(object)Lower256, (TVectorUInt16)(object)Upper256);
            }
            else if (typeof(TVectorByte) == typeof(Vector512<byte>))
            {
                (Vector512<ushort> Lower512, Vector512<ushort> Upper512) = Vector512.Widen((Vector512<byte>)(object)vector);
                return ((TVectorUInt16)(object)Lower512, (TVectorUInt16)(object)Upper512);
            }
            else
            {
                (Vector128<ushort> Lower128, Vector128<ushort> Upper128) = Vector128.Widen((Vector128<byte>)(object)vector);
                return ((TVectorUInt16)(object)Lower128, (TVectorUInt16)(object)Upper128);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void WidenAsciiToUtf1_Vector<TVectorByte, TVectorUInt16>(byte* pAsciiBuffer, char* pUtf16Buffer, ref nuint currentOffset, nuint elementCount)
            where TVectorByte : unmanaged, ISimdVector<TVectorByte, byte>
            where TVectorUInt16 : unmanaged, ISimdVector<TVectorUInt16, ushort>
        {
            ushort* pCurrentWriteAddress = (ushort*)pUtf16Buffer;
            // Calculating the destination address outside the loop results in significant
            // perf wins vs. relying on the JIT to fold memory addressing logic into the
            // write instructions. See: https://github.com/dotnet/runtime/issues/33002
            nuint finalOffsetWhereCanRunLoop = elementCount - (nuint)TVectorByte.ElementCount;
            TVectorByte asciiVector = TVectorByte.Load(pAsciiBuffer + currentOffset);
            if (!HasMatch<TVectorByte>(asciiVector))
            {
                (TVectorUInt16 utf16LowVector, TVectorUInt16 utf16HighVector) = Widen<TVectorByte, TVectorUInt16>(asciiVector);
                utf16LowVector.Store(pCurrentWriteAddress);
                utf16HighVector.Store(pCurrentWriteAddress + TVectorUInt16.ElementCount);
                pCurrentWriteAddress += (nuint)(TVectorUInt16.ElementCount * 2);
                if (((nuint)pCurrentWriteAddress % sizeof(char)) == 0)
                {
                    // Bump write buffer up to the next aligned boundary
                    pCurrentWriteAddress = (ushort*)((nuint)pCurrentWriteAddress & ~(nuint)(TVectorUInt16.Alignment - 1));
                    nuint numBytesWritten = (nuint)pCurrentWriteAddress - (nuint)pUtf16Buffer;
                    currentOffset += (nuint)numBytesWritten / 2;
                }
                else
                {
                    // If input isn't char aligned, we won't be able to align it to a Vector
                    currentOffset += (nuint)TVectorByte.ElementCount;
                }
                while (currentOffset <= finalOffsetWhereCanRunLoop)
                {
                    asciiVector = TVectorByte.Load(pAsciiBuffer + currentOffset);
                    if (HasMatch<TVectorByte>(asciiVector))
                    {
                        break;
                    }
                    (utf16LowVector, utf16HighVector) = Widen<TVectorByte, TVectorUInt16>(asciiVector);
                    utf16LowVector.Store(pCurrentWriteAddress);
                    utf16HighVector.Store(pCurrentWriteAddress + TVectorUInt16.ElementCount);

                    currentOffset += (nuint)TVectorByte.ElementCount;
                    pCurrentWriteAddress += (nuint)(TVectorUInt16.ElementCount * 2);
                }
            }
            return;
        }
    }
}

namespace System.Text.Unicode
{
    /// <summary>
    /// Provides static methods that convert chunked data between UTF-8 and UTF-16 encodings.
    /// </summary>
    public static class Utf8
    {
        /// <summary>
        /// Transcodes the UTF-16 <paramref name="source"/> buffer to <paramref name="destination"/> as UTF-8.
        /// </summary>
        /// <remarks>
        /// If <paramref name="replaceInvalidSequences"/> is <see langword="true"/>, invalid UTF-16 sequences
        /// in <paramref name="source"/> will be replaced with U+FFFD in <paramref name="destination"/>, and
        /// this method will not return <see cref="OperationStatus.InvalidData"/>.
        /// </remarks>
        public static unsafe OperationStatus FromUtf16(ReadOnlySpan<char> source, Span<byte> destination, out int charsRead, out int bytesWritten, bool replaceInvalidSequences = true, bool isFinalBlock = true)
        {
            fixed (char* pOriginalSource = &MemoryMarshal.GetReference(source))
            fixed (byte* pOriginalDestination = &MemoryMarshal.GetReference(destination))
            {
                // We're going to bulk transcode as much as we can in a loop, iterating
                // every time we see bad data that requires replacement.

                OperationStatus operationStatus = OperationStatus.Done;
                char* pInputBufferRemaining = pOriginalSource;
                byte* pOutputBufferRemaining = pOriginalDestination;

                while (!source.IsEmpty)
                {
                    // We've pinned the spans at the entry point to this method.
                    // It's safe for us to use Unsafe.AsPointer on them during this loop.

                    operationStatus = Utf8Utility.TranscodeToUtf8(
                        pInputBuffer: (char*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(source)),
                        inputLength: source.Length,
                        pOutputBuffer: (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(destination)),
                        outputBytesRemaining: destination.Length,
                        pInputBufferRemaining: out pInputBufferRemaining,
                        pOutputBufferRemaining: out pOutputBufferRemaining);

                    // If we finished the operation entirely or we ran out of space in the destination buffer,
                    // or if we need more input data and the caller told us that there's possibly more data
                    // coming, return immediately.

                    if (operationStatus <= OperationStatus.DestinationTooSmall
                        || (operationStatus == OperationStatus.NeedMoreData && !isFinalBlock))
                    {
                        break;
                    }

                    // We encountered invalid data, or we need more data but the caller told us we're
                    // at the end of the stream. In either case treat this as truly invalid.
                    // If the caller didn't tell us to replace invalid sequences, return immediately.

                    if (!replaceInvalidSequences)
                    {
                        operationStatus = OperationStatus.InvalidData; // status code may have been NeedMoreData - force to be error
                        break;
                    }

                    // We're going to attempt to write U+FFFD to the destination buffer.
                    // Do we even have enough space to do so?

                    destination = destination.Slice((int)(pOutputBufferRemaining - (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(destination))));

                    if (destination.Length <= 2)
                    {
                        operationStatus = OperationStatus.DestinationTooSmall;
                        break;
                    }

                    destination[0] = 0xEF; // U+FFFD = [ EF BF BD ] in UTF-8
                    destination[1] = 0xBF;
                    destination[2] = 0xBD;
                    destination = destination.Slice(3);

                    // Invalid UTF-16 sequences are always of length 1. Just skip the next character.

                    source = source.Slice((int)(pInputBufferRemaining - (char*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(source))) + 1);

                    operationStatus = OperationStatus.Done; // we patched the error - if we're about to break out of the loop this is a success case
                    pInputBufferRemaining = (char*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(source));
                    pOutputBufferRemaining = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(destination));
                }

                // Not possible to make any further progress - report to our caller how far we got.

                charsRead = (int)(pInputBufferRemaining - pOriginalSource);
                bytesWritten = (int)(pOutputBufferRemaining - pOriginalDestination);
                return operationStatus;
            }
        }

        /// <summary>
        /// Transcodes the UTF-8 <paramref name="source"/> buffer to <paramref name="destination"/> as UTF-16.
        /// </summary>
        /// <remarks>
        /// If <paramref name="replaceInvalidSequences"/> is <see langword="true"/>, invalid UTF-8 sequences
        /// in <paramref name="source"/> will be replaced with U+FFFD in <paramref name="destination"/>, and
        /// this method will not return <see cref="OperationStatus.InvalidData"/>.
        /// </remarks>
        public static unsafe OperationStatus ToUtf16(ReadOnlySpan<byte> source, Span<char> destination, out int bytesRead, out int charsWritten, bool replaceInvalidSequences = true, bool isFinalBlock = true)
        {
            // NOTE: Changes to this method should be kept in sync with ToUtf16PreservingReplacement below.
            // See it for an explanation of the differences

            // We'll be mutating these values throughout our loop.

            fixed (byte* pOriginalSource = &MemoryMarshal.GetReference(source))
            fixed (char* pOriginalDestination = &MemoryMarshal.GetReference(destination))
            {
                // We're going to bulk transcode as much as we can in a loop, iterating
                // every time we see bad data that requires replacement.

                OperationStatus operationStatus = OperationStatus.Done;
                byte* pInputBufferRemaining = pOriginalSource;
                char* pOutputBufferRemaining = pOriginalDestination;

                while (!source.IsEmpty)
                {
                    // We've pinned the spans at the entry point to this method.
                    // It's safe for us to use Unsafe.AsPointer on them during this loop.

                    operationStatus = Utf8Utility.TranscodeToUtf16(
                        pInputBuffer: (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(source)),
                        inputLength: source.Length,
                        pOutputBuffer: (char*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(destination)),
                        outputCharsRemaining: destination.Length,
                        pInputBufferRemaining: out pInputBufferRemaining,
                        pOutputBufferRemaining: out pOutputBufferRemaining);

                    // If we finished the operation entirely or we ran out of space in the destination buffer,
                    // or if we need more input data and the caller told us that there's possibly more data
                    // coming, return immediately.

                    if (operationStatus <= OperationStatus.DestinationTooSmall
                        || (operationStatus == OperationStatus.NeedMoreData && !isFinalBlock))
                    {
                        break;
                    }

                    // We encountered invalid data, or we need more data but the caller told us we're
                    // at the end of the stream. In either case treat this as truly invalid.
                    // If the caller didn't tell us to replace invalid sequences, return immediately.

                    if (!replaceInvalidSequences)
                    {
                        operationStatus = OperationStatus.InvalidData; // status code may have been NeedMoreData - force to be error
                        break;
                    }

                    // We're going to attempt to write U+FFFD to the destination buffer.
                    // Do we even have enough space to do so?

                    destination = destination.Slice((int)(pOutputBufferRemaining - (char*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(destination))));

                    if (destination.IsEmpty)
                    {
                        operationStatus = OperationStatus.DestinationTooSmall;
                        break;
                    }

                    destination[0] = (char)UnicodeUtility.ReplacementChar;
                    destination = destination.Slice(1);

                    // Now figure out how many bytes of the source we must skip over before we should retry
                    // the operation. This might be more than 1 byte.

                    source = source.Slice((int)(pInputBufferRemaining - (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(source))));

                    Rune.DecodeFromUtf8(source, out _, out int bytesConsumedJustNow);
                    source = source.Slice(bytesConsumedJustNow);

                    operationStatus = OperationStatus.Done; // we patched the error - if we're about to break out of the loop this is a success case
                    pInputBufferRemaining = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(source));
                    pOutputBufferRemaining = (char*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(destination));
                }

                // Not possible to make any further progress - report to our caller how far we got.

                bytesRead = (int)(pInputBufferRemaining - pOriginalSource);
                charsWritten = (int)(pOutputBufferRemaining - pOriginalDestination);
                return operationStatus;
            }
        }
    }
    internal static partial class Utf8Utility
    {
        /// <summary>
        /// The maximum number of bytes that can result from UTF-8 transcoding
        /// any Unicode scalar value.
        /// </summary>
        internal const int MaxBytesPerScalar = 4;

        /// <summary>
        /// Returns true iff the UInt32 represents four ASCII UTF-8 characters in machine endianness.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool AllBytesInUInt32AreAscii(uint value) => (value & ~0x7F7F_7F7Fu) == 0;
    }
    internal static unsafe partial class Utf8Utility
    {
        // On method return, pInputBufferRemaining and pOutputBufferRemaining will both point to where
        // the next byte would have been consumed from / the next char would have been written to.
        // inputLength in bytes, outputCharsRemaining in chars.
        public static OperationStatus TranscodeToUtf16(byte* pInputBuffer, int inputLength, char* pOutputBuffer, int outputCharsRemaining, out byte* pInputBufferRemaining, out char* pOutputBufferRemaining)
        {
            // First, try vectorized conversion.
            {
                nuint numElementsConverted = Ascii.WidenAsciiToUtf16(pInputBuffer, pOutputBuffer, (uint)Math.Min(inputLength, outputCharsRemaining));

                pInputBuffer += numElementsConverted;
                pOutputBuffer += numElementsConverted;

                // Quick check - did we just end up consuming the entire input buffer?
                // If so, short-circuit the remainder of the method.

                if ((int)numElementsConverted == inputLength)
                {
                    pInputBufferRemaining = pInputBuffer;
                    pOutputBufferRemaining = pOutputBuffer;
                    return OperationStatus.Done;
                }

                inputLength -= (int)numElementsConverted;
                outputCharsRemaining -= (int)numElementsConverted;
            }

            if (inputLength < sizeof(uint))
            {
                goto ProcessInputOfLessThanDWordSize;
            }

            byte* pFinalPosWhereCanReadDWordFromInputBuffer = pInputBuffer + (uint)inputLength - 4;

            // Begin the main loop.

            do
            {
                // Read 32 bits at a time. This is enough to hold any possible UTF8-encoded scalar.

                uint thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);

            AfterReadDWord:

                // First, check for the common case of all-ASCII bytes.

                if (Ascii.AllBytesInUInt32AreAscii(thisDWord))
                {
                    // We read an all-ASCII sequence.

                    if (outputCharsRemaining < sizeof(uint))
                    {
                        goto ProcessRemainingBytesSlow; // running out of space, but may be able to write some data
                    }

                    Ascii.WidenFourAsciiBytesToUtf16AndWriteToBuffer(ref *pOutputBuffer, thisDWord);
                    pInputBuffer += 4;
                    pOutputBuffer += 4;
                    outputCharsRemaining -= 4;

                    // If we saw a sequence of all ASCII, there's a good chance a significant amount of following data is also ASCII.
                    // Below is basically unrolled loops with poor man's vectorization.

                    uint remainingInputBytes = (uint)(void*)Unsafe.ByteOffset(ref *pInputBuffer, ref *pFinalPosWhereCanReadDWordFromInputBuffer) + 4;
                    uint maxIters = Math.Min(remainingInputBytes, (uint)outputCharsRemaining) / (2 * sizeof(uint));
                    uint secondDWord;
                    int i;
                    for (i = 0; (uint)i < maxIters; i++)
                    {
                        // Reading two DWORDs in parallel benchmarked faster than reading a single QWORD.

                        thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);
                        secondDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer + sizeof(uint));

                        if (!Ascii.AllBytesInUInt32AreAscii(thisDWord | secondDWord))
                        {
                            goto LoopTerminatedEarlyDueToNonAsciiData;
                        }

                        pInputBuffer += 8;

                        Ascii.WidenFourAsciiBytesToUtf16AndWriteToBuffer(ref pOutputBuffer[0], thisDWord);
                        Ascii.WidenFourAsciiBytesToUtf16AndWriteToBuffer(ref pOutputBuffer[4], secondDWord);

                        pOutputBuffer += 8;
                    }

                    outputCharsRemaining -= 8 * i;

                    continue; // need to perform a bounds check because we might be running out of data

                LoopTerminatedEarlyDueToNonAsciiData:

                    if (Ascii.AllBytesInUInt32AreAscii(thisDWord))
                    {
                        // The first DWORD contained all-ASCII bytes, so expand it.

                        Ascii.WidenFourAsciiBytesToUtf16AndWriteToBuffer(ref *pOutputBuffer, thisDWord);

                        // continue the outer loop from the second DWORD

                        thisDWord = secondDWord;

                        pInputBuffer += 4;
                        pOutputBuffer += 4;
                        outputCharsRemaining -= 4;
                    }

                    outputCharsRemaining -= 8 * i;

                    // We know that there's *at least* one DWORD of data remaining in the buffer.
                    // We also know that it's not all-ASCII. We can skip the logic at the beginning of the main loop.

                    goto AfterReadDWordSkipAllBytesAsciiCheck;
                }

            AfterReadDWordSkipAllBytesAsciiCheck:

// this should have been handled earlier

                // Next, try stripping off ASCII bytes one at a time.
                // We only handle up to three ASCII bytes here since we handled the four ASCII byte case above.

                if (UInt32FirstByteIsAscii(thisDWord))
                {
                    if (outputCharsRemaining >= 3)
                    {
                        // Fast-track: we don't need to check the destination length for subsequent
                        // ASCII bytes since we know we can write them all now.

                        uint thisDWordLittleEndian = ToLittleEndian(thisDWord);

                        nuint adjustment = 1;
                        pOutputBuffer[0] = (char)(byte)thisDWordLittleEndian;

                        if (UInt32SecondByteIsAscii(thisDWord))
                        {
                            adjustment++;
                            thisDWordLittleEndian >>= 8;
                            pOutputBuffer[1] = (char)(byte)thisDWordLittleEndian;

                            if (UInt32ThirdByteIsAscii(thisDWord))
                            {
                                adjustment++;
                                thisDWordLittleEndian >>= 8;
                                pOutputBuffer[2] = (char)(byte)thisDWordLittleEndian;
                            }
                        }

                        pInputBuffer += adjustment;
                        pOutputBuffer += adjustment;
                        outputCharsRemaining -= (int)adjustment;
                    }
                    else
                    {
                        // Slow-track: we need to make sure each individual write has enough
                        // of a buffer so that we don't overrun the destination.

                        if (outputCharsRemaining == 0)
                        {
                            goto OutputBufferTooSmall;
                        }

                        uint thisDWordLittleEndian = ToLittleEndian(thisDWord);

                        pInputBuffer++;
                        *pOutputBuffer++ = (char)(byte)thisDWordLittleEndian;
                        outputCharsRemaining--;

                        if (UInt32SecondByteIsAscii(thisDWord))
                        {
                            if (outputCharsRemaining == 0)
                            {
                                goto OutputBufferTooSmall;
                            }

                            pInputBuffer++;
                            thisDWordLittleEndian >>= 8;
                            *pOutputBuffer++ = (char)(byte)thisDWordLittleEndian;

                            // We can perform a small optimization here. We know at this point that
                            // the output buffer is fully consumed (we read two ASCII bytes and wrote
                            // two ASCII chars, and we checked earlier that the destination buffer
                            // can't store a third byte). If the next byte is ASCII, we can jump straight
                            // to the return statement since the end-of-method logic only relies on the
                            // destination buffer pointer -- NOT the output chars remaining count -- being
                            // correct. If the next byte is not ASCII, we'll need to continue with the
                            // rest of the main loop, but we can set the buffer length directly to zero
                            // rather than decrementing it from 1 to 0.

                            if (UInt32ThirdByteIsAscii(thisDWord))
                            {
                                goto OutputBufferTooSmall;
                            }
                            else
                            {
                                outputCharsRemaining = 0;
                            }
                        }
                    }

                    if (pInputBuffer > pFinalPosWhereCanReadDWordFromInputBuffer)
                    {
                        goto ProcessRemainingBytesSlow; // input buffer doesn't contain enough data to read a DWORD
                    }
                    else
                    {
                        // The input buffer at the current offset contains a non-ASCII byte.
                        // Read an entire DWORD and fall through to multi-byte consumption logic.
                        thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);
                    }
                }

            BeforeProcessTwoByteSequence:

                // At this point, we know we're working with a multi-byte code unit,
                // but we haven't yet validated it.

                // The masks and comparands are derived from the Unicode Standard, Table 3-6.
                // Additionally, we need to check for valid byte sequences per Table 3-7.

                // Check the 2-byte case.

                if (UInt32BeginsWithUtf8TwoByteMask(thisDWord))
                {
                    // Per Table 3-7, valid sequences are:
                    // [ C2..DF ] [ 80..BF ]

                    if (UInt32BeginsWithOverlongUtf8TwoByteSequence(thisDWord))
                    {
                        goto Error;
                    }

                ProcessTwoByteSequenceSkipOverlongFormCheck:

                    // Optimization: If this is a two-byte-per-character language like Cyrillic or Hebrew,
                    // there's a good chance that if we see one two-byte run then there's another two-byte
                    // run immediately after. Let's check that now.

                    // On little-endian platforms, we can check for the two-byte UTF8 mask *and* validate that
                    // the value isn't overlong using a single comparison. On big-endian platforms, we'll need
                    // to validate the mask and validate that the sequence isn't overlong as two separate comparisons.

                    if ((BitConverter.IsLittleEndian && UInt32EndsWithValidUtf8TwoByteSequenceLittleEndian(thisDWord))
                        || (!BitConverter.IsLittleEndian && (UInt32EndsWithUtf8TwoByteMask(thisDWord) && !UInt32EndsWithOverlongUtf8TwoByteSequence(thisDWord))))
                    {
                        // We have two runs of two bytes each.

                        if (outputCharsRemaining < 2)
                        {
                            goto ProcessRemainingBytesSlow; // running out of output buffer
                        }

                        Unsafe.WriteUnaligned(pOutputBuffer, ExtractTwoCharsPackedFromTwoAdjacentTwoByteSequences(thisDWord));

                        pInputBuffer += 4;
                        pOutputBuffer += 2;
                        outputCharsRemaining -= 2;

                        if (pInputBuffer <= pFinalPosWhereCanReadDWordFromInputBuffer)
                        {
                            // Optimization: If we read a long run of two-byte sequences, the next sequence is probably
                            // also two bytes. Check for that first before going back to the beginning of the loop.

                            thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);

                            if (BitConverter.IsLittleEndian)
                            {
                                if (UInt32BeginsWithValidUtf8TwoByteSequenceLittleEndian(thisDWord))
                                {
                                    // The next sequence is a valid two-byte sequence.
                                    goto ProcessTwoByteSequenceSkipOverlongFormCheck;
                                }
                            }
                            else
                            {
                                if (UInt32BeginsWithUtf8TwoByteMask(thisDWord))
                                {
                                    if (UInt32BeginsWithOverlongUtf8TwoByteSequence(thisDWord))
                                    {
                                        goto Error; // The next sequence purports to be a 2-byte sequence but is overlong.
                                    }

                                    goto ProcessTwoByteSequenceSkipOverlongFormCheck;
                                }
                            }

                            // If we reached this point, the next sequence is something other than a valid
                            // two-byte sequence, so go back to the beginning of the loop.
                            goto AfterReadDWord;
                        }
                        else
                        {
                            goto ProcessRemainingBytesSlow; // Running out of data - go down slow path
                        }
                    }

                    // The buffer contains a 2-byte sequence followed by 2 bytes that aren't a 2-byte sequence.
                    // Unlikely that a 3-byte sequence would follow a 2-byte sequence, so perhaps remaining
                    // bytes are ASCII?

                    uint charToWrite = ExtractCharFromFirstTwoByteSequence(thisDWord); // optimistically compute this now, but don't store until we know dest is large enough

                    if (UInt32ThirdByteIsAscii(thisDWord))
                    {
                        if (UInt32FourthByteIsAscii(thisDWord))
                        {
                            if (outputCharsRemaining < 3)
                            {
                                goto ProcessRemainingBytesSlow; // running out of output buffer
                            }

                            pOutputBuffer[0] = (char)charToWrite;
                            if (BitConverter.IsLittleEndian)
                            {
                                thisDWord >>= 16;
                                pOutputBuffer[1] = (char)(byte)thisDWord;
                                thisDWord >>= 8;
                                pOutputBuffer[2] = (char)thisDWord;
                            }
                            else
                            {
                                pOutputBuffer[2] = (char)(byte)thisDWord;
                                pOutputBuffer[1] = (char)(byte)(thisDWord >> 8);
                            }
                            pInputBuffer += 4;
                            pOutputBuffer += 3;
                            outputCharsRemaining -= 3;

                            continue; // go back to original bounds check and check for ASCII
                        }
                        else
                        {
                            if (outputCharsRemaining < 2)
                            {
                                goto ProcessRemainingBytesSlow; // running out of output buffer
                            }

                            pOutputBuffer[0] = (char)charToWrite;
                            pOutputBuffer[1] = (char)(byte)(thisDWord >> (BitConverter.IsLittleEndian ? 16 : 8));
                            pInputBuffer += 3;
                            pOutputBuffer += 2;
                            outputCharsRemaining -= 2;

                            // A two-byte sequence followed by an ASCII byte followed by a non-ASCII byte.
                            // Read in the next DWORD and jump directly to the start of the multi-byte processing block.

                            if (pFinalPosWhereCanReadDWordFromInputBuffer < pInputBuffer)
                            {
                                goto ProcessRemainingBytesSlow; // Running out of data - go down slow path
                            }
                            else
                            {
                                thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);
                                goto BeforeProcessTwoByteSequence;
                            }
                        }
                    }
                    else
                    {
                        if (outputCharsRemaining == 0)
                        {
                            goto ProcessRemainingBytesSlow; // running out of output buffer
                        }

                        pOutputBuffer[0] = (char)charToWrite;
                        pInputBuffer += 2;
                        pOutputBuffer++;
                        outputCharsRemaining--;

                        if (pFinalPosWhereCanReadDWordFromInputBuffer < pInputBuffer)
                        {
                            goto ProcessRemainingBytesSlow; // Running out of data - go down slow path
                        }
                        else
                        {
                            thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);
                            goto BeforeProcessThreeByteSequence; // we know the next byte isn't ASCII, and it's not the start of a 2-byte sequence (this was checked above)
                        }
                    }
                }

            // Check the 3-byte case.

            BeforeProcessThreeByteSequence:

                if (UInt32BeginsWithUtf8ThreeByteMask(thisDWord))
                {
                ProcessThreeByteSequenceWithCheck:

                    // We need to check for overlong or surrogate three-byte sequences.
                    //
                    // Per Table 3-7, valid sequences are:
                    // [   E0   ] [ A0..BF ] [ 80..BF ]
                    // [ E1..EC ] [ 80..BF ] [ 80..BF ]
                    // [   ED   ] [ 80..9F ] [ 80..BF ]
                    // [ EE..EF ] [ 80..BF ] [ 80..BF ]
                    //
                    // Big-endian examples of using the above validation table:
                    // E0A0 = 1110 0000 1010 0000 => invalid (overlong ) patterns are 1110 0000 100# ####
                    // ED9F = 1110 1101 1001 1111 => invalid (surrogate) patterns are 1110 1101 101# ####
                    // If using the bitmask ......................................... 0000 1111 0010 0000 (=0F20),
                    // Then invalid (overlong) patterns match the comparand ......... 0000 0000 0000 0000 (=0000),
                    // And invalid (surrogate) patterns match the comparand ......... 0000 1101 0010 0000 (=0D20).

                    if (BitConverter.IsLittleEndian)
                    {
                        // The "overlong or surrogate" check can be implemented using a single jump, but there's
                        // some overhead to moving the bits into the correct locations in order to perform the
                        // correct comparison, and in practice the processor's branch prediction capability is
                        // good enough that we shouldn't bother. So we'll use two jumps instead.

                        // Can't extract this check into its own helper method because JITter produces suboptimal
                        // assembly, even with aggressive inlining.

                        // Code below becomes 5 instructions: test, jz, lea, test, jz

                        if (((thisDWord & 0x0000_200Fu) == 0) || (((thisDWord - 0x0000_200Du) & 0x0000_200Fu) == 0))
                        {
                            goto Error; // overlong or surrogate
                        }
                    }
                    else
                    {
                        if (((thisDWord & 0x0F20_0000u) == 0) || (((thisDWord - 0x0D20_0000u) & 0x0F20_0000u) == 0))
                        {
                            goto Error; // overlong or surrogate
                        }
                    }

                    // At this point, we know the incoming scalar is well-formed.

                    if (outputCharsRemaining == 0)
                    {
                        goto OutputBufferTooSmall; // not enough space in the destination buffer to write
                    }

                    // As an optimization, on compatible platforms check if a second three-byte sequence immediately
                    // follows the one we just read, and if so extract them together.

                    if (BitConverter.IsLittleEndian)
                    {
                        // First, check that the leftover byte from the original DWORD is in the range [ E0..EF ], which
                        // would indicate the potential start of a second three-byte sequence.

                        if (((thisDWord - 0xE000_0000u) & 0xF000_0000u) == 0)
                        {
                            // The const '3' below is correct because pFinalPosWhereCanReadDWordFromInputBuffer represents
                            // the final place where we can safely perform a DWORD read, and we want to probe whether it's
                            // safe to read a DWORD beginning at address &pInputBuffer[3].

                            if (outputCharsRemaining > 1 && (nint)(void*)Unsafe.ByteOffset(ref *pInputBuffer, ref *pFinalPosWhereCanReadDWordFromInputBuffer) >= 3)
                            {
                                // We're going to attempt to read a second 3-byte sequence and write them both out one after the other.
                                // We need to check the continuation bit mask on the remaining two bytes (and we may as well check the leading
                                // byte mask again since it's free), then perform overlong + surrogate checks. If the overlong or surrogate
                                // checks fail, we'll fall through to the remainder of the logic which will transcode the original valid
                                // 3-byte UTF-8 sequence we read; and on the next iteration of the loop the validation routine will run again,
                                // fail, and redirect control flow to the error handling logic at the very end of this method.

                                uint secondDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer + 3);

                                if (UInt32BeginsWithUtf8ThreeByteMask(secondDWord)
                                    && ((secondDWord & 0x0000_200Fu) != 0)
                                    && (((secondDWord - 0x0000_200Du) & 0x0000_200Fu) != 0))
                                {
                                    pOutputBuffer[0] = (char)ExtractCharFromFirstThreeByteSequence(thisDWord);
                                    pOutputBuffer[1] = (char)ExtractCharFromFirstThreeByteSequence(secondDWord);
                                    pInputBuffer += 6;
                                    pOutputBuffer += 2;
                                    outputCharsRemaining -= 2;

                                    // Drain any ASCII data following the second three-byte sequence.

                                    goto CheckForAsciiByteAfterThreeByteSequence;
                                }
                            }
                        }
                    }

                    // Couldn't extract 2x three-byte sequences together, just do this one by itself.

                    *pOutputBuffer = (char)ExtractCharFromFirstThreeByteSequence(thisDWord);
                    pInputBuffer += 3;
                    pOutputBuffer++;
                    outputCharsRemaining--;

                CheckForAsciiByteAfterThreeByteSequence:

                    // Occasionally one-off ASCII characters like spaces, periods, or newlines will make their way
                    // in to the text. If this happens strip it off now before seeing if the next character
                    // consists of three code units.

                    if (UInt32FourthByteIsAscii(thisDWord))
                    {
                        if (outputCharsRemaining == 0)
                        {
                            goto OutputBufferTooSmall;
                        }

                        if (BitConverter.IsLittleEndian)
                        {
                            *pOutputBuffer = (char)(thisDWord >> 24);
                        }
                        else
                        {
                            *pOutputBuffer = (char)(byte)thisDWord;
                        }

                        pInputBuffer++;
                        pOutputBuffer++;
                        outputCharsRemaining--;
                    }

                    if (pInputBuffer <= pFinalPosWhereCanReadDWordFromInputBuffer)
                    {
                        thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);

                        // Optimization: A three-byte character could indicate CJK text, which makes it likely
                        // that the character following this one is also CJK. We'll check for a three-byte sequence
                        // marker now and jump directly to three-byte sequence processing if we see one, skipping
                        // all of the logic at the beginning of the loop.

                        if (UInt32BeginsWithUtf8ThreeByteMask(thisDWord))
                        {
                            goto ProcessThreeByteSequenceWithCheck; // found a three-byte sequence marker; validate and consume
                        }
                        else
                        {
                            goto AfterReadDWord; // probably ASCII punctuation or whitespace
                        }
                    }
                    else
                    {
                        goto ProcessRemainingBytesSlow; // Running out of data - go down slow path
                    }
                }

                // Assume the 4-byte case, but we need to validate.

                {
                    // We need to check for overlong or invalid (over U+10FFFF) four-byte sequences.
                    //
                    // Per Table 3-7, valid sequences are:
                    // [   F0   ] [ 90..BF ] [ 80..BF ] [ 80..BF ]
                    // [ F1..F3 ] [ 80..BF ] [ 80..BF ] [ 80..BF ]
                    // [   F4   ] [ 80..8F ] [ 80..BF ] [ 80..BF ]

                    if (!UInt32BeginsWithUtf8FourByteMask(thisDWord))
                    {
                        goto Error;
                    }

                    // Now check for overlong / out-of-range sequences.

                    if (BitConverter.IsLittleEndian)
                    {
                        // The DWORD we read is [ 10xxxxxx 10yyyyyy 10zzzzzz 11110www ].
                        // We want to get the 'w' byte in front of the 'z' byte so that we can perform
                        // a single range comparison. We'll take advantage of the fact that the JITter
                        // can detect a ROR / ROL operation, then we'll just zero out the bytes that
                        // aren't involved in the range check.

                        uint toCheck = thisDWord & 0x0000_FFFFu;

                        // At this point, toCheck = [ 00000000 00000000 10zzzzzz 11110www ].

                        toCheck = BitOperations.RotateRight(toCheck, 8);

                        // At this point, toCheck = [ 11110www 00000000 00000000 10zzzzzz ].

                        if (!UnicodeUtility.IsInRangeInclusive(toCheck, 0xF000_0090u, 0xF400_008Fu))
                        {
                            goto Error;
                        }
                    }
                    else
                    {
                        if (!UnicodeUtility.IsInRangeInclusive(thisDWord, 0xF090_0000u, 0xF48F_FFFFu))
                        {
                            goto Error;
                        }
                    }

                    // Validation complete.

                    if (outputCharsRemaining < 2)
                    {
                        // There's no point to falling back to the "drain the input buffer" logic, since we know
                        // we can't write anything to the destination. So we'll just exit immediately.
                        goto OutputBufferTooSmall;
                    }

                    Unsafe.WriteUnaligned(pOutputBuffer, ExtractCharsFromFourByteSequence(thisDWord));

                    pInputBuffer += 4;
                    pOutputBuffer += 2;
                    outputCharsRemaining -= 2;

                    continue; // go back to beginning of loop for processing
                }
            } while (pInputBuffer <= pFinalPosWhereCanReadDWordFromInputBuffer);

        ProcessRemainingBytesSlow:
            inputLength = (int)(void*)Unsafe.ByteOffset(ref *pInputBuffer, ref *pFinalPosWhereCanReadDWordFromInputBuffer) + 4;

        ProcessInputOfLessThanDWordSize:
            while (inputLength > 0)
            {
                uint firstByte = pInputBuffer[0];
                if (firstByte <= 0x7Fu)
                {
                    if (outputCharsRemaining == 0)
                    {
                        goto OutputBufferTooSmall; // we have no hope of writing anything to the output
                    }

                    // 1-byte (ASCII) case
                    *pOutputBuffer = (char)firstByte;

                    pInputBuffer++;
                    pOutputBuffer++;
                    inputLength--;
                    outputCharsRemaining--;
                    continue;
                }

                // Potentially the start of a multi-byte sequence?

                firstByte -= 0xC2u;
                if ((byte)firstByte <= (0xDFu - 0xC2u))
                {
                    // Potentially a 2-byte sequence?
                    if (inputLength < 2)
                    {
                        goto InputBufferTooSmall; // out of data
                    }

                    uint secondByte = pInputBuffer[1];
                    if (!IsLowByteUtf8ContinuationByte(secondByte))
                    {
                        goto Error; // 2-byte marker not followed by continuation byte
                    }

                    if (outputCharsRemaining == 0)
                    {
                        goto OutputBufferTooSmall; // we have no hope of writing anything to the output
                    }

                    uint asChar = (firstByte << 6) + secondByte + ((0xC2u - 0xC0u) << 6) - 0x80u; // remove UTF-8 markers from scalar
                    *pOutputBuffer = (char)asChar;

                    pInputBuffer += 2;
                    pOutputBuffer++;
                    inputLength -= 2;
                    outputCharsRemaining--;
                    continue;
                }
                else if ((byte)firstByte <= (0xEFu - 0xC2u))
                {
                    // Potentially a 3-byte sequence?
                    if (inputLength >= 3)
                    {
                        uint secondByte = pInputBuffer[1];
                        uint thirdByte = pInputBuffer[2];
                        if (!IsLowByteUtf8ContinuationByte(secondByte) || !IsLowByteUtf8ContinuationByte(thirdByte))
                        {
                            goto Error; // 3-byte marker not followed by 2 continuation bytes
                        }

                        // To speed up the validation logic below, we're not going to remove the UTF-8 markers from the partial char just yet.
                        // We account for this in the comparisons below.

                        uint partialChar = (firstByte << 12) + (secondByte << 6);
                        if (partialChar < ((0xE0u - 0xC2u) << 12) + (0xA0u << 6))
                        {
                            goto Error; // this is an overlong encoding; fail
                        }

                        partialChar -= ((0xEDu - 0xC2u) << 12) + (0xA0u << 6); // if partialChar = 0, we're at beginning of UTF-16 surrogate code point range
                        if (partialChar < 0x0800u /* number of code points in UTF-16 surrogate code point range */)
                        {
                            goto Error; // attempted to encode a UTF-16 surrogate code point; fail
                        }

                        if (outputCharsRemaining == 0)
                        {
                            goto OutputBufferTooSmall; // we have no hope of writing anything to the output
                        }

                        // Now restore the full scalar value.

                        partialChar += thirdByte;
                        partialChar += 0xD800; // undo "move to beginning of UTF-16 surrogate code point range" from earlier, fold it with later adds
                        partialChar -= 0x80u; // remove third byte continuation marker

                        *pOutputBuffer = (char)partialChar;

                        pInputBuffer += 3;
                        pOutputBuffer++;
                        inputLength -= 3;
                        outputCharsRemaining--;
                        continue;
                    }
                    else if (inputLength >= 2)
                    {
                        uint secondByte = pInputBuffer[1];
                        if (!IsLowByteUtf8ContinuationByte(secondByte))
                        {
                            goto Error; // 3-byte marker not followed by continuation byte
                        }

                        // We can't build up the entire scalar value now, but we can check for overlong / surrogate representations
                        // from just the first two bytes.

                        uint partialChar = (firstByte << 6) + secondByte; // don't worry about fixing up the UTF-8 markers; we'll account for it in the below comparison
                        if (partialChar < ((0xE0u - 0xC2u) << 6) + 0xA0u)
                        {
                            goto Error; // failed overlong check
                        }
                        if (UnicodeUtility.IsInRangeInclusive(partialChar, ((0xEDu - 0xC2u) << 6) + 0xA0u, ((0xEEu - 0xC2u) << 6) + 0x7Fu))
                        {
                            goto Error; // failed surrogate check
                        }
                    }

                    goto InputBufferTooSmall; // out of data
                }
                else if ((byte)firstByte <= (0xF4u - 0xC2u))
                {
                    // Potentially a 4-byte sequence?

                    if (inputLength < 2)
                    {
                        goto InputBufferTooSmall; // ran out of data
                    }

                    uint nextByte = pInputBuffer[1];
                    if (!IsLowByteUtf8ContinuationByte(nextByte))
                    {
                        goto Error; // 4-byte marker not followed by a continuation byte
                    }

                    uint asPartialChar = (firstByte << 6) + nextByte; // don't worry about fixing up the UTF-8 markers; we'll account for it in the below comparison
                    if (!UnicodeUtility.IsInRangeInclusive(asPartialChar, ((0xF0u - 0xC2u) << 6) + 0x90u, ((0xF4u - 0xC2u) << 6) + 0x8Fu))
                    {
                        goto Error; // failed overlong / out-of-range check
                    }

                    if (inputLength < 3)
                    {
                        goto InputBufferTooSmall; // ran out of data
                    }

                    if (!IsLowByteUtf8ContinuationByte(pInputBuffer[2]))
                    {
                        goto Error; // third byte in 4-byte sequence not a continuation byte
                    }

                    if (inputLength < 4)
                    {
                        goto InputBufferTooSmall; // ran out of data
                    }

                    if (!IsLowByteUtf8ContinuationByte(pInputBuffer[3]))
                    {
                        goto Error; // fourth byte in 4-byte sequence not a continuation byte
                    }

                    // If we read a valid astral scalar value, the only way we could've fallen down this code path
                    // is that we didn't have enough output buffer to write the result.

                    goto OutputBufferTooSmall;
                }
                else
                {
                    goto Error; // didn't begin with [ C2 .. F4 ], so invalid multi-byte sequence header byte
                }
            }

            OperationStatus retVal = OperationStatus.Done;
            goto ReturnCommon;

        InputBufferTooSmall:
            retVal = OperationStatus.NeedMoreData;
            goto ReturnCommon;

        OutputBufferTooSmall:
            retVal = OperationStatus.DestinationTooSmall;
            goto ReturnCommon;

        Error:
            retVal = OperationStatus.InvalidData;
            goto ReturnCommon;

        ReturnCommon:
            pInputBufferRemaining = pInputBuffer;
            pOutputBufferRemaining = pOutputBuffer;
            return retVal;
        }

        // On method return, pInputBufferRemaining and pOutputBufferRemaining will both point to where
        // the next char would have been consumed from / the next byte would have been written to.
        // inputLength in chars, outputBytesRemaining in bytes.
        public static OperationStatus TranscodeToUtf8(char* pInputBuffer, int inputLength, byte* pOutputBuffer, int outputBytesRemaining, out char* pInputBufferRemaining, out byte* pOutputBufferRemaining)
        {
            const int CharsPerDWord = sizeof(uint) / sizeof(char);

            // First, try vectorized conversion.

            {
                nuint numElementsConverted = Ascii.NarrowUtf16ToAscii(pInputBuffer, pOutputBuffer, (uint)Math.Min(inputLength, outputBytesRemaining));

                pInputBuffer += numElementsConverted;
                pOutputBuffer += numElementsConverted;

                // Quick check - did we just end up consuming the entire input buffer?
                // If so, short-circuit the remainder of the method.

                if ((int)numElementsConverted == inputLength)
                {
                    pInputBufferRemaining = pInputBuffer;
                    pOutputBufferRemaining = pOutputBuffer;
                    return OperationStatus.Done;
                }

                inputLength -= (int)numElementsConverted;
                outputBytesRemaining -= (int)numElementsConverted;
            }

            if (inputLength < CharsPerDWord)
            {
                goto ProcessInputOfLessThanDWordSize;
            }

            char* pFinalPosWhereCanReadDWordFromInputBuffer = pInputBuffer + (uint)inputLength - CharsPerDWord;

            // We have paths for SSE4.1 vectorization inside the inner loop. Since the below
            // vector is only used in those code paths, we leave it uninitialized if SSE4.1
            // is not enabled.

            Vector128<short> nonAsciiUtf16DataMask;

            if (Sse41.X64.IsSupported || (AdvSimd.Arm64.IsSupported && BitConverter.IsLittleEndian))
            {
                nonAsciiUtf16DataMask = Vector128.Create(unchecked((short)0xFF80)); // mask of non-ASCII bits in a UTF-16 char
            }

            // Begin the main loop.

            uint thisDWord;

            do
            {
                // Read 32 bits at a time. This is enough to hold any possible UTF16-encoded scalar.

                thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);

            AfterReadDWord:

                // First, check for the common case of all-ASCII chars.

                if (Utf16Utility.AllCharsInUInt32AreAscii(thisDWord))
                {
                    // We read an all-ASCII sequence (2 chars).

                    if (outputBytesRemaining < 2)
                    {
                        goto ProcessOneCharFromCurrentDWordAndFinish; // running out of space, but may be able to write some data
                    }

                    // The high WORD of the local declared below might be populated with garbage
                    // as a result of our shifts below, but that's ok since we're only going to
                    // write the low WORD.
                    //
                    // [ 00000000 0bbbbbbb | 00000000 0aaaaaaa ] -> [ 00000000 0bbbbbbb | 0bbbbbbb 0aaaaaaa ]
                    // (Same logic works regardless of endianness.)
                    uint valueToWrite = thisDWord | (thisDWord >> 8);

                    Unsafe.WriteUnaligned(pOutputBuffer, (ushort)valueToWrite);

                    pInputBuffer += 2;
                    pOutputBuffer += 2;
                    outputBytesRemaining -= 2;

                    // If we saw a sequence of all ASCII, there's a good chance a significant amount of following data is also ASCII.
                    // Below is basically unrolled loops with poor man's vectorization.

                    uint inputCharsRemaining = (uint)(pFinalPosWhereCanReadDWordFromInputBuffer - pInputBuffer) + 2;
                    uint minElementsRemaining = (uint)Math.Min(inputCharsRemaining, outputBytesRemaining);

                    if (Sse41.X64.IsSupported || (AdvSimd.Arm64.IsSupported && BitConverter.IsLittleEndian))
                    {
                        // Try reading and writing 8 elements per iteration.
                        uint maxIters = minElementsRemaining / 8;
                        ulong possibleNonAsciiQWord;
                        int i;
                        Vector128<short> utf16Data;
                        for (i = 0; (uint)i < maxIters; i++)
                        {
                            // The trimmer won't trim out nonAsciiUtf16DataMask unless this is in the loop.
                            // Luckily, this is a nop and will be elided by the JIT
                            Unsafe.SkipInit(out nonAsciiUtf16DataMask);

                            utf16Data = Unsafe.ReadUnaligned<Vector128<short>>(pInputBuffer);

                            if (AdvSimd.Arm64.IsSupported)
                            {
                                Vector128<short> isUtf16DataNonAscii = AdvSimd.CompareTest(utf16Data, nonAsciiUtf16DataMask);
                                bool hasNonAsciiDataInVector = AdvSimd.Arm64.MinPairwise(isUtf16DataNonAscii, isUtf16DataNonAscii).AsUInt64().ToScalar() != 0;

                                if (hasNonAsciiDataInVector)
                                {
                                    goto LoopTerminatedDueToNonAsciiDataInVectorLocal;
                                }

                                Vector64<byte> lower = AdvSimd.ExtractNarrowingSaturateUnsignedLower(utf16Data);
                                AdvSimd.Store(pOutputBuffer, lower);
                            }
                            else if (Sse41.IsSupported)
                            {
                                if ((utf16Data & nonAsciiUtf16DataMask) != Vector128<short>.Zero)
                                {
                                    goto LoopTerminatedDueToNonAsciiDataInVectorLocal;
                                }

                                // narrow and write
                                Sse2.StoreScalar((ulong*)pOutputBuffer /* unaligned */, Sse2.PackUnsignedSaturate(utf16Data, utf16Data).AsUInt64());
                            }
                            else
                            {
                                // We explicitly recheck each IsSupported query to ensure that the trimmer can see which paths are live/dead
                                ThrowHelper.ThrowUnreachableException();
                            }

                            pInputBuffer += 8;
                            pOutputBuffer += 8;
                        }

                        outputBytesRemaining -= 8 * i;

                        // Can we perform one more iteration, but reading & writing 4 elements instead of 8?

                        if ((minElementsRemaining & 4) != 0)
                        {
                            possibleNonAsciiQWord = Unsafe.ReadUnaligned<ulong>(pInputBuffer);
                            if (!Utf16Utility.AllCharsInUInt64AreAscii(possibleNonAsciiQWord))
                            {
                                goto LoopTerminatedDueToNonAsciiDataInPossibleNonAsciiQWordLocal;
                            }

                            utf16Data = Vector128.CreateScalarUnsafe(possibleNonAsciiQWord).AsInt16();

                            if (AdvSimd.IsSupported)
                            {
                                Vector64<byte> lower = AdvSimd.ExtractNarrowingSaturateUnsignedLower(utf16Data);
                                AdvSimd.StoreSelectedScalar((uint*)pOutputBuffer, lower.AsUInt32(), 0);
                            }
                            else if (Sse2.IsSupported)
                            {
                                Unsafe.WriteUnaligned(pOutputBuffer, Sse2.ConvertToUInt32(Sse2.PackUnsignedSaturate(utf16Data, utf16Data).AsUInt32()));
                            }
                            else
                            {
                                // We explicitly recheck each IsSupported query to ensure that the trimmer can see which paths are live/dead
                                ThrowHelper.ThrowUnreachableException();
                            }

                            pInputBuffer += 4;
                            pOutputBuffer += 4;
                            outputBytesRemaining -= 4;
                        }

                        continue; // Go back to beginning of main loop, read data, check for ASCII

                    LoopTerminatedDueToNonAsciiDataInVectorLocal:

                        outputBytesRemaining -= 8 * i;

                        if (Sse2.X64.IsSupported)
                        {
                            possibleNonAsciiQWord = Sse2.X64.ConvertToUInt64(utf16Data.AsUInt64());
                        }
                        else
                        {
                            possibleNonAsciiQWord = utf16Data.AsUInt64().ToScalar();
                        }

                        // Temporarily set 'possibleNonAsciiQWord' to be the low 64 bits of the vector,
                        // then check whether it's all-ASCII. If so, narrow and write to the destination
                        // buffer. Since we know that either the high 64 bits or the low 64 bits of the
                        // vector contains non-ASCII data, by the end of the following block the
                        // 'possibleNonAsciiQWord' local is guaranteed to contain the non-ASCII segment.

                        if (Utf16Utility.AllCharsInUInt64AreAscii(possibleNonAsciiQWord)) // all chars in first QWORD are ASCII
                        {
                            if (AdvSimd.IsSupported)
                            {
                                Vector64<byte> lower = AdvSimd.ExtractNarrowingSaturateUnsignedLower(utf16Data);
                                AdvSimd.StoreSelectedScalar((uint*)pOutputBuffer, lower.AsUInt32(), 0);
                            }
                            else if (Sse2.IsSupported)
                            {
                                Unsafe.WriteUnaligned(pOutputBuffer, Sse2.ConvertToUInt32(Sse2.PackUnsignedSaturate(utf16Data, utf16Data).AsUInt32()));
                            }
                            else
                            {
                                // We explicitly recheck each IsSupported query to ensure that the trimmer can see which paths are live/dead
                                ThrowHelper.ThrowUnreachableException();
                            }
                            pInputBuffer += 4;
                            pOutputBuffer += 4;
                            outputBytesRemaining -= 4;
                            possibleNonAsciiQWord = utf16Data.AsUInt64().GetElement(1);
                        }

                    LoopTerminatedDueToNonAsciiDataInPossibleNonAsciiQWordLocal:

// this condition should've been checked earlier

                        thisDWord = (uint)possibleNonAsciiQWord;
                        if (Utf16Utility.AllCharsInUInt32AreAscii(thisDWord))
                        {
                            // [ 00000000 0bbbbbbb | 00000000 0aaaaaaa ] -> [ 00000000 0bbbbbbb | 0bbbbbbb 0aaaaaaa ]
                            Unsafe.WriteUnaligned(pOutputBuffer, (ushort)(thisDWord | (thisDWord >> 8)));
                            pInputBuffer += 2;
                            pOutputBuffer += 2;
                            outputBytesRemaining -= 2;
                            thisDWord = (uint)(possibleNonAsciiQWord >> 32);
                        }

                        goto AfterReadDWordSkipAllCharsAsciiCheck;
                    }
                    else
                    {
                        // Can't use SSE41 x64, so we'll only read and write 4 elements per iteration.
                        uint maxIters = minElementsRemaining / 4;
                        uint secondDWord;
                        int i;
                        for (i = 0; (uint)i < maxIters; i++)
                        {
                            thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);
                            secondDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer + 2);

                            if (!Utf16Utility.AllCharsInUInt32AreAscii(thisDWord | secondDWord))
                            {
                                goto LoopTerminatedDueToNonAsciiData;
                            }

                            // [ 00000000 0bbbbbbb | 00000000 0aaaaaaa ] -> [ 00000000 0bbbbbbb | 0bbbbbbb 0aaaaaaa ]
                            // (Same logic works regardless of endianness.)
                            Unsafe.WriteUnaligned(pOutputBuffer, (ushort)(thisDWord | (thisDWord >> 8)));
                            Unsafe.WriteUnaligned(pOutputBuffer + 2, (ushort)(secondDWord | (secondDWord >> 8)));

                            pInputBuffer += 4;
                            pOutputBuffer += 4;
                        }

                        outputBytesRemaining -= 4 * i;

                        continue; // Go back to beginning of main loop, read data, check for ASCII

                    LoopTerminatedDueToNonAsciiData:

                        outputBytesRemaining -= 4 * i;

                        // First, see if we can drain any ASCII data from the first DWORD.

                        if (Utf16Utility.AllCharsInUInt32AreAscii(thisDWord))
                        {
                            // [ 00000000 0bbbbbbb | 00000000 0aaaaaaa ] -> [ 00000000 0bbbbbbb | 0bbbbbbb 0aaaaaaa ]
                            // (Same logic works regardless of endianness.)
                            Unsafe.WriteUnaligned(pOutputBuffer, (ushort)(thisDWord | (thisDWord >> 8)));
                            pInputBuffer += 2;
                            pOutputBuffer += 2;
                            outputBytesRemaining -= 2;
                            thisDWord = secondDWord;
                        }

                        goto AfterReadDWordSkipAllCharsAsciiCheck;
                    }
                }

            AfterReadDWordSkipAllCharsAsciiCheck:

// this should have been handled earlier

                // Next, try stripping off the first ASCII char if it exists.
                // We don't check for a second ASCII char since that should have been handled above.

                if (IsFirstCharAscii(thisDWord))
                {
                    if (outputBytesRemaining == 0)
                    {
                        goto OutputBufferTooSmall;
                    }

                    if (BitConverter.IsLittleEndian)
                    {
                        pOutputBuffer[0] = (byte)thisDWord; // extract [ ## ## 00 AA ]
                    }
                    else
                    {
                        pOutputBuffer[0] = (byte)(thisDWord >> 16); // extract [ 00 AA ## ## ]
                    }

                    pInputBuffer++;
                    pOutputBuffer++;
                    outputBytesRemaining--;

                    if (pInputBuffer > pFinalPosWhereCanReadDWordFromInputBuffer)
                    {
                        goto ProcessNextCharAndFinish; // input buffer doesn't contain enough data to read a DWORD
                    }
                    else
                    {
                        // The input buffer at the current offset contains a non-ASCII char.
                        // Read an entire DWORD and fall through to non-ASCII consumption logic.
                        thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);
                    }
                }

                // At this point, we know the first char in the buffer is non-ASCII, but we haven't yet validated it.

                if (!IsFirstCharAtLeastThreeUtf8Bytes(thisDWord))
                {
                TryConsumeMultipleTwoByteSequences:

                    // For certain text (Greek, Cyrillic, ...), 2-byte sequences tend to be clustered. We'll try transcoding them in
                    // a tight loop without falling back to the main loop.

                    if (IsSecondCharTwoUtf8Bytes(thisDWord))
                    {
                        // We have two runs of two bytes each.

                        if (outputBytesRemaining < 4)
                        {
                            goto ProcessOneCharFromCurrentDWordAndFinish; // running out of output buffer
                        }

                        Unsafe.WriteUnaligned(pOutputBuffer, ExtractTwoUtf8TwoByteSequencesFromTwoPackedUtf16Chars(thisDWord));

                        pInputBuffer += 2;
                        pOutputBuffer += 4;
                        outputBytesRemaining -= 4;

                        if (pInputBuffer > pFinalPosWhereCanReadDWordFromInputBuffer)
                        {
                            goto ProcessNextCharAndFinish; // Running out of data - go down slow path
                        }
                        else
                        {
                            // Optimization: If we read a long run of two-byte sequences, the next sequence is probably
                            // also two bytes. Check for that first before going back to the beginning of the loop.

                            thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);

                            if (IsFirstCharTwoUtf8Bytes(thisDWord))
                            {
                                // Validated we have a two-byte sequence coming up
                                goto TryConsumeMultipleTwoByteSequences;
                            }

                            // If we reached this point, the next sequence is something other than a valid
                            // two-byte sequence, so go back to the beginning of the loop.
                            goto AfterReadDWord;
                        }
                    }

                    if (outputBytesRemaining < 2)
                    {
                        goto OutputBufferTooSmall;
                    }

                    Unsafe.WriteUnaligned(pOutputBuffer, (ushort)ExtractUtf8TwoByteSequenceFromFirstUtf16Char(thisDWord));

                    // The buffer contains a 2-byte sequence followed by 2 bytes that aren't a 2-byte sequence.
                    // Unlikely that a 3-byte sequence would follow a 2-byte sequence, so perhaps remaining
                    // char is ASCII?

                    if (IsSecondCharAscii(thisDWord))
                    {
                        if (outputBytesRemaining >= 3)
                        {
                            if (BitConverter.IsLittleEndian)
                            {
                                thisDWord >>= 16;
                            }
                            pOutputBuffer[2] = (byte)thisDWord;

                            pInputBuffer += 2;
                            pOutputBuffer += 3;
                            outputBytesRemaining -= 3;

                            continue; // go back to original bounds check and check for ASCII
                        }
                        else
                        {
                            pInputBuffer++;
                            pOutputBuffer += 2;
                            goto OutputBufferTooSmall;
                        }
                    }
                    else
                    {
                        pInputBuffer++;
                        pOutputBuffer += 2;
                        outputBytesRemaining -= 2;

                        if (pInputBuffer > pFinalPosWhereCanReadDWordFromInputBuffer)
                        {
                            goto ProcessNextCharAndFinish; // Running out of data - go down slow path
                        }
                        else
                        {
                            thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);
                            goto BeforeProcessThreeByteSequence; // we know the next byte isn't ASCII, and it's not the start of a 2-byte sequence (this was checked above)
                        }
                    }
                }

            // Check the 3-byte case.

            BeforeProcessThreeByteSequence:

                if (!IsFirstCharSurrogate(thisDWord))
                {
                    // Optimization: A three-byte character could indicate CJK text, which makes it likely
                    // that the character following this one is also CJK. We'll perform the check now
                    // rather than jumping to the beginning of the main loop.

                    if (IsSecondCharAtLeastThreeUtf8Bytes(thisDWord))
                    {
                        if (!IsSecondCharSurrogate(thisDWord))
                        {
                            if (outputBytesRemaining < 6)
                            {
                                goto ConsumeSingleThreeByteRun; // not enough space - try consuming as much as we can
                            }

                            WriteTwoUtf16CharsAsTwoUtf8ThreeByteSequences(ref *pOutputBuffer, thisDWord);

                            pInputBuffer += 2;
                            pOutputBuffer += 6;
                            outputBytesRemaining -= 6;

                            // Try to remain in the 3-byte processing loop if at all possible.

                            if (pInputBuffer > pFinalPosWhereCanReadDWordFromInputBuffer)
                            {
                                goto ProcessNextCharAndFinish; // Running out of data - go down slow path
                            }
                            else
                            {
                                thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);

                                if (IsFirstCharAtLeastThreeUtf8Bytes(thisDWord))
                                {
                                    goto BeforeProcessThreeByteSequence;
                                }
                                else
                                {
                                    // Fall back to standard processing loop since we don't know how to optimize this.
                                    goto AfterReadDWord;
                                }
                            }
                        }
                    }

                ConsumeSingleThreeByteRun:

                    if (outputBytesRemaining < 3)
                    {
                        goto OutputBufferTooSmall;
                    }

                    WriteFirstUtf16CharAsUtf8ThreeByteSequence(ref *pOutputBuffer, thisDWord);

                    pInputBuffer++;
                    pOutputBuffer += 3;
                    outputBytesRemaining -= 3;

                    // Occasionally one-off ASCII characters like spaces, periods, or newlines will make their way
                    // in to the text. If this happens strip it off now before seeing if the next character
                    // consists of three code units.

                    if (IsSecondCharAscii(thisDWord))
                    {
                        if (outputBytesRemaining == 0)
                        {
                            goto OutputBufferTooSmall;
                        }

                        if (BitConverter.IsLittleEndian)
                        {
                            *pOutputBuffer = (byte)(thisDWord >> 16);
                        }
                        else
                        {
                            *pOutputBuffer = (byte)(thisDWord);
                        }

                        pInputBuffer++;
                        pOutputBuffer++;
                        outputBytesRemaining--;

                        if (pInputBuffer > pFinalPosWhereCanReadDWordFromInputBuffer)
                        {
                            goto ProcessNextCharAndFinish; // Running out of data - go down slow path
                        }
                        else
                        {
                            thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);

                            if (IsFirstCharAtLeastThreeUtf8Bytes(thisDWord))
                            {
                                goto BeforeProcessThreeByteSequence;
                            }
                            else
                            {
                                // Fall back to standard processing loop since we don't know how to optimize this.
                                goto AfterReadDWord;
                            }
                        }
                    }

                    if (pInputBuffer > pFinalPosWhereCanReadDWordFromInputBuffer)
                    {
                        goto ProcessNextCharAndFinish; // Running out of data - go down slow path
                    }
                    else
                    {
                        thisDWord = Unsafe.ReadUnaligned<uint>(pInputBuffer);
                        goto AfterReadDWordSkipAllCharsAsciiCheck; // we just checked above that this value isn't ASCII
                    }
                }

                // Four byte sequence processing

                if (IsWellFormedUtf16SurrogatePair(thisDWord))
                {
                    if (outputBytesRemaining < 4)
                    {
                        goto OutputBufferTooSmall;
                    }

                    Unsafe.WriteUnaligned(pOutputBuffer, ExtractFourUtf8BytesFromSurrogatePair(thisDWord));

                    pInputBuffer += 2;
                    pOutputBuffer += 4;
                    outputBytesRemaining -= 4;

                    continue; // go back to beginning of loop for processing
                }

                goto Error; // an ill-formed surrogate sequence: high not followed by low, or low not preceded by high
            } while (pInputBuffer <= pFinalPosWhereCanReadDWordFromInputBuffer);

        ProcessNextCharAndFinish:
            inputLength = (int)(pFinalPosWhereCanReadDWordFromInputBuffer - pInputBuffer) + CharsPerDWord;

        ProcessInputOfLessThanDWordSize:

            if (inputLength == 0)
            {
                goto InputBufferFullyConsumed;
            }

            uint thisChar = *pInputBuffer;
            goto ProcessFinalChar;

        ProcessOneCharFromCurrentDWordAndFinish:
            if (BitConverter.IsLittleEndian)
            {
                thisChar = thisDWord & 0xFFFFu; // preserve only the first char
            }
            else
            {
                thisChar = thisDWord >> 16; // preserve only the first char
            }

        ProcessFinalChar:
            {
                if (thisChar <= 0x7Fu)
                {
                    if (outputBytesRemaining == 0)
                    {
                        goto OutputBufferTooSmall; // we have no hope of writing anything to the output
                    }

                    // 1-byte (ASCII) case
                    *pOutputBuffer = (byte)thisChar;

                    pInputBuffer++;
                    pOutputBuffer++;
                }
                else if (thisChar < 0x0800u)
                {
                    if (outputBytesRemaining < 2)
                    {
                        goto OutputBufferTooSmall; // we have no hope of writing anything to the output
                    }

                    // 2-byte case
                    pOutputBuffer[1] = (byte)((thisChar & 0x3Fu) | unchecked((uint)(sbyte)0x80)); // [ 10xxxxxx ]
                    pOutputBuffer[0] = (byte)((thisChar >> 6) | unchecked((uint)(sbyte)0xC0)); // [ 110yyyyy ]

                    pInputBuffer++;
                    pOutputBuffer += 2;
                }
                else if (!UnicodeUtility.IsSurrogateCodePoint(thisChar))
                {
                    if (outputBytesRemaining < 3)
                    {
                        goto OutputBufferTooSmall; // we have no hope of writing anything to the output
                    }

                    // 3-byte case
                    pOutputBuffer[2] = (byte)((thisChar & 0x3Fu) | unchecked((uint)(sbyte)0x80)); // [ 10xxxxxx ]
                    pOutputBuffer[1] = (byte)(((thisChar >> 6) & 0x3Fu) | unchecked((uint)(sbyte)0x80)); // [ 10yyyyyy ]
                    pOutputBuffer[0] = (byte)((thisChar >> 12) | unchecked((uint)(sbyte)0xE0)); // [ 1110zzzz ]

                    pInputBuffer++;
                    pOutputBuffer += 3;
                }
                else if (thisChar <= 0xDBFFu)
                {
                    // UTF-16 high surrogate code point with no trailing data, report incomplete input buffer
                    goto InputBufferTooSmall;
                }
                else
                {
                    // UTF-16 low surrogate code point with no leading data, report error
                    goto Error;
                }
            }

            // There are two ways we can end up here. Either we were running low on input data,
            // or we were running low on space in the destination buffer. If we're running low on
            // input data (label targets ProcessInputOfLessThanDWordSize and ProcessNextCharAndFinish),
            // then the inputLength value is guaranteed to be between 0 and 1, and we should return Done.
            // If we're running low on destination buffer space (label target ProcessOneCharFromCurrentDWordAndFinish),
            // then we didn't modify inputLength since entering the main loop, which means it should
            // still have a value of >= 2. So checking the value of inputLength is all we need to do to determine
            // which of the two scenarios we're in.

            if (inputLength > 1)
            {
                goto OutputBufferTooSmall;
            }

        InputBufferFullyConsumed:
            OperationStatus retVal = OperationStatus.Done;
            goto ReturnCommon;

        InputBufferTooSmall:
            retVal = OperationStatus.NeedMoreData;
            goto ReturnCommon;

        OutputBufferTooSmall:
            retVal = OperationStatus.DestinationTooSmall;
            goto ReturnCommon;

        Error:
            retVal = OperationStatus.InvalidData;
            goto ReturnCommon;

        ReturnCommon:
            pInputBufferRemaining = pInputBuffer;
            pOutputBufferRemaining = pOutputBuffer;
            return retVal;
        }
    }
    internal static partial class Utf8Utility
    {
        /// <summary>
        /// Given a machine-endian DWORD which four bytes of UTF-8 data, interprets the
        /// first three bytes as a three-byte UTF-8 subsequence and returns the UTF-16 representation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ExtractCharFromFirstThreeByteSequence(uint value)
        {
            if (BitConverter.IsLittleEndian)
            {
                // value = [ ######## | 10xxxxxx 10yyyyyy 1110zzzz ]
                return ((value & 0x003F0_000u) >> 16)
                    | ((value & 0x0000_3F00u) >> 2)
                    | ((value & 0x0000_000Fu) << 12);
            }
            else
            {
                // value = [ 1110zzzz 10yyyyyy 10xxxxxx | ######## ]
                return ((value & 0x0F00_0000u) >> 12)
                    | ((value & 0x003F_0000u) >> 10)
                    | ((value & 0x0000_3F00u) >> 8);
            }
        }

        /// <summary>
        /// Given a machine-endian DWORD which four bytes of UTF-8 data, interprets the
        /// first two bytes as a two-byte UTF-8 subsequence and returns the UTF-16 representation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ExtractCharFromFirstTwoByteSequence(uint value)
        {
            if (BitConverter.IsLittleEndian)
            {
                // value = [ ######## ######## | 10xxxxxx 110yyyyy ]
                uint leadingByte = (uint)(byte)value << 6;
                return (uint)(byte)(value >> 8) + leadingByte - (0xC0u << 6) - 0x80u; // remove header bits
            }
            else
            {
                // value = [ 110yyyyy 10xxxxxx | ######## ######## ]
                return (char)(((value & 0x1F00_0000u) >> 18) | ((value & 0x003F_0000u) >> 16));
            }
        }

        /// <summary>
        /// Given a machine-endian DWORD which represents four bytes of UTF-8 data, interprets the input as a
        /// four-byte UTF-8 sequence and returns the machine-endian DWORD of the UTF-16 representation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ExtractCharsFromFourByteSequence(uint value)
        {
            if (BitConverter.IsLittleEndian)
            {
                // input is UTF8 [ 10xxxxxx 10yyyyyy 10uuzzzz 11110uuu ] = scalar 000uuuuu zzzzyyyy yyxxxxxx
                // want to return UTF16 scalar 000uuuuuzzzzyyyyyyxxxxxx = [ 110111yy yyxxxxxx 110110ww wwzzzzyy ]
                // where wwww = uuuuu - 1
                uint retVal = (uint)(byte)value << 8; // retVal = [ 00000000 00000000 11110uuu 00000000 ]
                retVal |= (value & 0x0000_3F00u) >> 6; // retVal = [ 00000000 00000000 11110uuu uuzzzz00 ]
                retVal |= (value & 0x0030_0000u) >> 20; // retVal = [ 00000000 00000000 11110uuu uuzzzzyy ]
                retVal |= (value & 0x3F00_0000u) >> 8; // retVal = [ 00000000 00xxxxxx 11110uuu uuzzzzyy ]
                retVal |= (value & 0x000F_0000u) << 6; // retVal = [ 000000yy yyxxxxxx 11110uuu uuzzzzyy ]
                retVal -= 0x0000_0040u; // retVal = [ 000000yy yyxxxxxx 111100ww wwzzzzyy ]
                retVal -= 0x0000_2000u; // retVal = [ 000000yy yyxxxxxx 110100ww wwzzzzyy ]
                retVal += 0x0000_0800u; // retVal = [ 000000yy yyxxxxxx 110110ww wwzzzzyy ]
                retVal += 0xDC00_0000u; // retVal = [ 110111yy yyxxxxxx 110110ww wwzzzzyy ]
                return retVal;
            }
            else
            {
                // input is UTF8 [ 11110uuu 10uuzzzz 10yyyyyy 10xxxxxx ] = scalar 000uuuuu zzzzyyyy yyxxxxxx
                // want to return UTF16 scalar 000uuuuuxxxxxxxxxxxxxxxx = [ 110110wwwwxxxxxx 110111xxxxxxxxx ]
                // where wwww = uuuuu - 1
                uint retVal = value & 0xFF00_0000u; // retVal = [ 11110uuu 00000000 00000000 00000000 ]
                retVal |= (value & 0x003F_0000u) << 2; // retVal = [ 11110uuu uuzzzz00 00000000 00000000 ]
                retVal |= (value & 0x0000_3000u) << 4; // retVal = [ 11110uuu uuzzzzyy 00000000 00000000 ]
                retVal |= (value & 0x0000_0F00u) >> 2; // retVal = [ 11110uuu uuzzzzyy 000000yy yy000000 ]
                retVal |= (value & 0x0000_003Fu); // retVal = [ 11110uuu uuzzzzyy 000000yy yyxxxxxx ]
                retVal -= 0x2000_0000u; // retVal = [ 11010uuu uuzzzzyy 000000yy yyxxxxxx ]
                retVal -= 0x0040_0000u; // retVal = [ 110100ww wwzzzzyy 000000yy yyxxxxxx ]
                retVal += 0x0000_DC00u; // retVal = [ 110100ww wwzzzzyy 110111yy yyxxxxxx ]
                retVal += 0x0800_0000u; // retVal = [ 110110ww wwzzzzyy 110111yy yyxxxxxx ]
                return retVal;
            }
        }

        /// <summary>
        /// Given a 32-bit integer that represents a valid packed UTF-16 surrogate pair, all in machine-endian order,
        /// returns the packed 4-byte UTF-8 representation of this scalar value, also in machine-endian order.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ExtractFourUtf8BytesFromSurrogatePair(uint value)
        {
            if (BitConverter.IsLittleEndian)
            {
                // input = [ 110111yyyyxxxxxx 110110wwwwzzzzyy ] = scalar (000uuuuu zzzzyyyy yyxxxxxx)
                // must return [ 10xxxxxx 10yyyyyy 10uuzzzz 11110uuu ], where wwww = uuuuu - 1

                value += 0x0000_0040u; // = [ 110111yyyyxxxxxx 11011uuuuuzzzzyy ]

                uint tempA = BinaryPrimitives.ReverseEndianness(value & 0x003F_0700u); // = [ 00000000 00000uuu 00xxxxxx 00000000 ]
                tempA = BitOperations.RotateLeft(tempA, 16); // = [ 00xxxxxx 00000000 00000000 00000uuu ]

                uint tempB = (value & 0x00FCu) << 6; // = [ 00000000 00000000 00uuzzzz 00000000 ]
                uint tempC = (value >> 6) & 0x000F_0000u; // = [ 00000000 0000yyyy 00000000 00000000 ]
                tempC |= tempB;

                uint tempD = (value & 0x03u) << 20; // = [ 00000000 00yy0000 00000000 00000000 ]
                tempD |= 0x8080_80F0u;

                return tempD | tempA | tempC; // = [ 10xxxxxx 10yyyyyy 10uuzzzz 11110uuu ]
            }
            else
            {
                // input = [ 110110wwwwzzzzyy 110111yyyyxxxxxx ], where wwww = uuuuu - 1
                // must return [ 11110uuu 10uuzzzz 10yyyyyy 10xxxxxx ], where wwww = uuuuu - 1

                value -= 0xD800_DC00u; // = [ 000000wwwwzzzzyy 000000yyyyxxxxxx ]
                value += 0x0040_0000u; // = [ 00000uuuuuzzzzyy 000000yyyyxxxxxx ]

                uint tempA = value & 0x0700_0000u; // = [ 00000uuu 00000000 00000000 00000000 ]
                uint tempB = (value >> 2) & 0x003F_0000u; // = [ 00000000 00uuzzzz 00000000 00000000 ]
                tempB |= tempA;

                uint tempC = (value << 2) & 0x0000_0F00u; // = [ 00000000 00000000 0000yyyy 00000000 ]
                uint tempD = (value >> 4) & 0x0000_3000u; // = [ 00000000 00000000 00yy0000 00000000 ]
                tempD |= tempC;

                uint tempE = (value & 0x3Fu) + 0xF080_8080u; // = [ 11110000 10000000 10000000 10xxxxxx ]
                return tempE | tempB | tempD; // = [ 11110uuu 10uuzzzz 10yyyyyy 10xxxxxx ]
            }
        }

        /// <summary>
        /// Given a machine-endian DWORD which represents two adjacent UTF-8 two-byte sequences,
        /// returns the machine-endian DWORD representation of that same data as two adjacent
        /// UTF-16 byte sequences.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ExtractTwoCharsPackedFromTwoAdjacentTwoByteSequences(uint value)
        {
            // We don't want to swap the position of the high and low WORDs,
            // as the buffer was read in machine order and will be written in
            // machine order.

            if (BitConverter.IsLittleEndian)
            {
                // value = [ 10xxxxxx 110yyyyy | 10xxxxxx 110yyyyy ]
                return ((value & 0x3F003F00u) >> 8) | ((value & 0x001F001Fu) << 6);
            }
            else
            {
                // value = [ 110yyyyy 10xxxxxx | 110yyyyy 10xxxxxx ]
                return ((value & 0x1F001F00u) >> 2) | (value & 0x003F003Fu);
            }
        }

        /// <summary>
        /// Given a machine-endian DWORD which represents two adjacent UTF-16 sequences,
        /// returns the machine-endian DWORD representation of that same data as two
        /// adjacent UTF-8 two-byte sequences.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ExtractTwoUtf8TwoByteSequencesFromTwoPackedUtf16Chars(uint value)
        {
            // stays in machine endian

            if (BitConverter.IsLittleEndian)
            {
                // value = [ 00000YYY YYXXXXXX 00000yyy yyxxxxxx ]
                // want to return [ 10XXXXXX 110YYYYY 10xxxxxx 110yyyyy ]

                return ((value >> 6) & 0x001F_001Fu) + ((value << 8) & 0x3F00_3F00u) + 0x80C0_80C0u;
            }
            else
            {
                // value = [ 00000YYY YYXXXXXX 00000yyy yyxxxxxx ]
                // want to return [ 110YYYYY 10XXXXXX 110yyyyy 10xxxxxx ]

                return ((value << 2) & 0x1F00_1F00u) + (value & 0x003F_003Fu) + 0xC080_C080u;
            }
        }

        /// <summary>
        /// Given a machine-endian DWORD which represents two adjacent UTF-16 sequences,
        /// returns the machine-endian DWORD representation of the first UTF-16 char
        /// as a UTF-8 two-byte sequence packed into a WORD and zero-extended to DWORD.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ExtractUtf8TwoByteSequenceFromFirstUtf16Char(uint value)
        {
            // stays in machine endian

            if (BitConverter.IsLittleEndian)
            {
                // value = [ ######## ######## 00000yyy yyxxxxxx ]
                // want to return [ ######## ######## 10xxxxxx 110yyyyy ]

                uint temp = (value << 2) & 0x1F00u; // [ 00000000 00000000 000yyyyy 00000000 ]
                value &= 0x3Fu; // [ 00000000 00000000 00000000 00xxxxxx ]
                return BinaryPrimitives.ReverseEndianness((ushort)(temp + value + 0xC080u)); // [ 00000000 00000000 10xxxxxx 110yyyyy ]
            }
            else
            {
                // value = [ 00000yyy yyxxxxxx ######## ######## ]
                // want to return [ ######## ######## 110yyyyy 10xxxxxx ]

                uint temp = (value >> 16) & 0x3Fu; // [ 00000000 00000000 00000000 00xxxxxx ]
                value = (value >> 14) & 0x1F00u; // [ 00000000 00000000 000yyyyy 0000000 ]
                return value + temp + 0xC080u;
            }
        }

        /// <summary>
        /// Given a 32-bit integer that represents two packed UTF-16 characters, all in machine-endian order,
        /// returns true iff the first UTF-16 character is ASCII.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFirstCharAscii(uint value)
        {
            // Little-endian: Given [ #### AAAA ], return whether AAAA is in range [ 0000..007F ].
            // Big-endian: Given [ AAAA #### ], return whether AAAA is in range [ 0000..007F ].

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && (value & 0xFF80u) == 0)
                || (!BitConverter.IsLittleEndian && value < 0x0080_0000u);
        }

        /// <summary>
        /// Given a 32-bit integer that represents two packed UTF-16 characters, all in machine-endian order,
        /// returns true iff the first UTF-16 character requires *at least* 3 bytes to encode in UTF-8.
        /// This also returns true if the first UTF-16 character is a surrogate character (well-formedness is not validated).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFirstCharAtLeastThreeUtf8Bytes(uint value)
        {
            // Little-endian: Given [ #### AAAA ], return whether AAAA is in range [ 0800..FFFF ].
            // Big-endian: Given [ AAAA #### ], return whether AAAA is in range [ 0800..FFFF ].

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && (value & 0xF800u) != 0)
                || (!BitConverter.IsLittleEndian && value >= 0x0800_0000u);
        }

        /// <summary>
        /// Given a 32-bit integer that represents two packed UTF-16 characters, all in machine-endian order,
        /// returns true iff the first UTF-16 character is a surrogate character (either high or low).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFirstCharSurrogate(uint value)
        {
            // Little-endian: Given [ #### AAAA ], return whether AAAA is in range [ D800..DFFF ].
            // Big-endian: Given [ AAAA #### ], return whether AAAA is in range [ D800..DFFF ].

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && ((value - 0xD800u) & 0xF800u) == 0)
                || (!BitConverter.IsLittleEndian && (value - 0xD800_0000u) < 0x0800_0000u);
        }

        /// <summary>
        /// Given a 32-bit integer that represents two packed UTF-16 characters, all in machine-endian order,
        /// returns true iff the first UTF-16 character would be encoded as exactly 2 bytes in UTF-8.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFirstCharTwoUtf8Bytes(uint value)
        {
            // Little-endian: Given [ #### AAAA ], return whether AAAA is in range [ 0080..07FF ].
            // Big-endian: Given [ AAAA #### ], return whether AAAA is in range [ 0080..07FF ].

            // TODO: I'd like to be able to write "(ushort)(value - 0x0080u) < 0x0780u" for the little-endian
            // case, but the JIT doesn't currently emit 16-bit comparisons efficiently.
            // Tracked as https://github.com/dotnet/runtime/issues/10337.

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && ((value - 0x0080u) & 0xFFFFu) < 0x0780u)
                || (!BitConverter.IsLittleEndian && UnicodeUtility.IsInRangeInclusive(value, 0x0080_0000u, 0x07FF_FFFFu));
        }

        /// <summary>
        /// Returns <see langword="true"/> iff the low byte of <paramref name="value"/>
        /// is a UTF-8 continuation byte.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLowByteUtf8ContinuationByte(uint value)
        {
            // The JIT won't emit a single 8-bit signed cmp instruction (see IsUtf8ContinuationByte),
            // so the best we can do for now is the lea / cmp pair.
            // Tracked as https://github.com/dotnet/runtime/issues/10337.

            return (byte)(value - 0x80u) <= 0x3Fu;
        }

        /// <summary>
        /// Given a 32-bit integer that represents two packed UTF-16 characters, all in machine-endian order,
        /// returns true iff the second UTF-16 character is ASCII.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSecondCharAscii(uint value)
        {
            // Little-endian: Given [ BBBB #### ], return whether BBBB is in range [ 0000..007F ].
            // Big-endian: Given [ #### BBBB ], return whether BBBB is in range [ 0000..007F ].

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && value < 0x0080_0000u)
                || (!BitConverter.IsLittleEndian && (value & 0xFF80u) == 0);
        }

        /// <summary>
        /// Given a 32-bit integer that represents two packed UTF-16 characters, all in machine-endian order,
        /// returns true iff the second UTF-16 character requires *at least* 3 bytes to encode in UTF-8.
        /// This also returns true if the second UTF-16 character is a surrogate character (well-formedness is not validated).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSecondCharAtLeastThreeUtf8Bytes(uint value)
        {
            // Little-endian: Given [ BBBB #### ], return whether BBBB is in range [ 0800..FFFF ].
            // Big-endian: Given [ #### BBBB ], return whether ABBBBAAA is in range [ 0800..FFFF ].

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && (value & 0xF800_0000u) != 0)
                || (!BitConverter.IsLittleEndian && (value & 0xF800u) != 0);
        }

        /// <summary>
        /// Given a 32-bit integer that represents two packed UTF-16 characters, all in machine-endian order,
        /// returns true iff the second UTF-16 character is a surrogate character (either high or low).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSecondCharSurrogate(uint value)
        {
            // Little-endian: Given [ BBBB #### ], return whether BBBB is in range [ D800..DFFF ].
            // Big-endian: Given [ #### BBBB ], return whether BBBB is in range [ D800..DFFF ].

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && (value - 0xD800_0000u) < 0x0800_0000u)
                || (!BitConverter.IsLittleEndian && ((value - 0xD800u) & 0xF800u) == 0);
        }

        /// <summary>
        /// Given a 32-bit integer that represents two packed UTF-16 characters, all in machine-endian order,
        /// returns true iff the second UTF-16 character would be encoded as exactly 2 bytes in UTF-8.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSecondCharTwoUtf8Bytes(uint value)
        {
            // Little-endian: Given [ BBBB #### ], return whether BBBB is in range [ 0080..07FF ].
            // Big-endian: Given [ #### BBBB ], return whether BBBB is in range [ 0080..07FF ].

            // TODO: I'd like to be able to write "(ushort)(value - 0x0080u) < 0x0780u" for the big-endian
            // case, but the JIT doesn't currently emit 16-bit comparisons efficiently.
            // Tracked as https://github.com/dotnet/runtime/issues/10337.

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && UnicodeUtility.IsInRangeInclusive(value, 0x0080_0000u, 0x07FF_FFFFu))
                || (!BitConverter.IsLittleEndian && ((value - 0x0080u) & 0xFFFFu) < 0x0780u);
        }

        /// <summary>
        /// Returns <see langword="true"/> iff <paramref name="value"/> is a UTF-8 continuation byte;
        /// i.e., has binary representation 10xxxxxx, where x is any bit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsUtf8ContinuationByte(in byte value)
        {
            // This API takes its input as a readonly ref so that the JIT can emit "cmp ModRM" statements
            // directly rather than bounce a temporary through a register. That is, we want the JIT to be
            // able to emit a single "cmp byte ptr [data], C0h" statement if we're querying a memory location
            // to see if it's a continuation byte. Data that's already enregistered will go through the
            // normal "cmp reg, C0h" code paths, perhaps with some extra unnecessary "movzx" instructions.
            //
            // The below check takes advantage of the two's complement representation of negative numbers.
            // [ 0b1000_0000, 0b1011_1111 ] is [ -127 (sbyte.MinValue), -65 ]

            return (sbyte)value < -64;
        }

        /// <summary>
        /// Given a 32-bit integer that represents two packed UTF-16 characters, all in machine-endian order,
        /// returns true iff the two characters represent a well-formed UTF-16 surrogate pair.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWellFormedUtf16SurrogatePair(uint value)
        {
            // Little-endian: Given [ LLLL HHHH ], validate that LLLL in [ DC00..DFFF ] and HHHH in [ D800..DBFF ].
            // Big-endian: Given [ HHHH LLLL ], validate that HHHH in [ D800..DBFF ] and LLLL in [ DC00..DFFF ].
            //
            // We're essentially performing a range check on each component of the input in parallel. The allowed range
            // ends up being "< 0x0400" after the beginning of the allowed range is subtracted from each element. We
            // can't perform the equivalent of two CMPs in parallel, but we can take advantage of the fact that 0x0400
            // is a whole power of 2, which means that a CMP is really just a glorified TEST operation. Two TESTs *can*
            // be performed in parallel. The logic below then becomes 3 operations: "add/lea; test; jcc".

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && ((value - 0xDC00_D800u) & 0xFC00_FC00u) == 0)
                || (!BitConverter.IsLittleEndian && ((value - 0xD800_DC00u) & 0xFC00_FC00u) == 0);
        }

        /// <summary>
        /// Converts a DWORD from machine-endian to little-endian.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ToLittleEndian(uint value)
        {
            if (BitConverter.IsLittleEndian)
            {
                return value;
            }
            else
            {
                return BinaryPrimitives.ReverseEndianness(value);
            }
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD in machine endianness,
        /// returns <see langword="true"/> iff the first two bytes of the buffer are
        /// an overlong representation of a sequence that should be represented as one byte.
        /// This method *does not* validate that the sequence matches the appropriate
        /// 2-byte sequence mask (see <see cref="UInt32BeginsWithUtf8TwoByteMask"/>).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32BeginsWithOverlongUtf8TwoByteSequence(uint value)
        {
            // ASSUMPTION: Caller has already checked the '110yyyyy 10xxxxxx' mask of the input.

            // Per Table 3-7, first byte of two-byte sequence must be within range C2 .. DF.
            // Since we already validated it's 80 <= ?? <= DF (per mask check earlier), now only need
            // to check that it's < C2.

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && ((byte)value < 0xC2u))
                || (!BitConverter.IsLittleEndian && (value < 0xC200_0000u));
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD in machine endianness,
        /// returns <see langword="true"/> iff the first four bytes of the buffer match
        /// the UTF-8 4-byte sequence mask [ 11110www 10zzzzzz 10yyyyyy 10xxxxxx ]. This
        /// method *does not* validate that the sequence is well-formed; the caller must
        /// still perform overlong form or out-of-range checking.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32BeginsWithUtf8FourByteMask(uint value)
        {
            // The code in this method is equivalent to the code
            // below but is slightly more optimized.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     const uint mask = 0xC0C0C0F8U;
            //     const uint comparand = 0x808080F0U;
            //     return ((value & mask) == comparand);
            // }
            // else
            // {
            //     const uint mask = 0xF8C0C0C0U;
            //     const uint comparand = 0xF0808000U;
            //     return ((value & mask) == comparand);
            // }

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && (((value - 0x8080_80F0u) & 0xC0C0_C0F8u) == 0))
                || (!BitConverter.IsLittleEndian && (((value - 0xF080_8080u) & 0xF8C0_C0C0u) == 0));
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD in machine endianness,
        /// returns <see langword="true"/> iff the first three bytes of the buffer match
        /// the UTF-8 3-byte sequence mask [ 1110zzzz 10yyyyyy 10xxxxxx ]. This method *does not*
        /// validate that the sequence is well-formed; the caller must still perform
        /// overlong form or surrogate checking.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32BeginsWithUtf8ThreeByteMask(uint value)
        {
            // The code in this method is equivalent to the code
            // below but is slightly more optimized.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     const uint mask = 0x00C0C0F0U;
            //     const uint comparand = 0x008080E0U;
            //     return ((value & mask) == comparand);
            // }
            // else
            // {
            //     const uint mask = 0xF0C0C000U;
            //     const uint comparand = 0xE0808000U;
            //     return ((value & mask) == comparand);
            // }

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && (((value - 0x0080_80E0u) & 0x00C0_C0F0u) == 0))
                || (!BitConverter.IsLittleEndian && (((value - 0xE080_8000u) & 0xF0C0_C000u) == 0));
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD in machine endianness,
        /// returns <see langword="true"/> iff the first two bytes of the buffer match
        /// the UTF-8 2-byte sequence mask [ 110yyyyy 10xxxxxx ]. This method *does not*
        /// validate that the sequence is well-formed; the caller must still perform
        /// overlong form checking.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32BeginsWithUtf8TwoByteMask(uint value)
        {
            // The code in this method is equivalent to the code
            // below but is slightly more optimized.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     const uint mask = 0x0000C0E0U;
            //     const uint comparand = 0x000080C0U;
            //     return ((value & mask) == comparand);
            // }
            // else
            // {
            //     const uint mask = 0xE0C00000U;
            //     const uint comparand = 0xC0800000U;
            //     return ((value & mask) == comparand);
            // }

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && (((value - 0x0000_80C0u) & 0x0000_C0E0u) == 0))
                || (!BitConverter.IsLittleEndian && (((value - 0xC080_0000u) & 0xE0C0_0000u) == 0));
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD in machine endianness,
        /// returns <see langword="true"/> iff the first two bytes of the buffer are
        /// an overlong representation of a sequence that should be represented as one byte.
        /// This method *does not* validate that the sequence matches the appropriate
        /// 2-byte sequence mask (see <see cref="UInt32BeginsWithUtf8TwoByteMask"/>).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32EndsWithOverlongUtf8TwoByteSequence(uint value)
        {
            // ASSUMPTION: Caller has already checked the '110yyyyy 10xxxxxx' mask of the input.

            // Per Table 3-7, first byte of two-byte sequence must be within range C2 .. DF.
            // We already validated that it's 80 .. DF (per mask check earlier).
            // C2 = 1100 0010
            // DF = 1101 1111
            // This means that we can AND the leading byte with the mask 0001 1110 (1E),
            // and if the result is zero the sequence is overlong.

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && ((value & 0x001E_0000u) == 0))
                || (!BitConverter.IsLittleEndian && ((value & 0x1E00u) == 0));
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD in machine endianness,
        /// returns <see langword="true"/> iff the last two bytes of the buffer match
        /// the UTF-8 2-byte sequence mask [ 110yyyyy 10xxxxxx ]. This method *does not*
        /// validate that the sequence is well-formed; the caller must still perform
        /// overlong form checking.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32EndsWithUtf8TwoByteMask(uint value)
        {
            // The code in this method is equivalent to the code
            // below but is slightly more optimized.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     const uint mask = 0xC0E00000U;
            //     const uint comparand = 0x80C00000U;
            //     return ((value & mask) == comparand);
            // }
            // else
            // {
            //     const uint mask = 0x0000E0C0U;
            //     const uint comparand = 0x0000C080U;
            //     return ((value & mask) == comparand);
            // }

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && (((value - 0x80C0_0000u) & 0xC0E0_0000u) == 0))
                || (!BitConverter.IsLittleEndian && (((value - 0x0000_C080u) & 0x0000_E0C0u) == 0));
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD on a little-endian machine,
        /// returns <see langword="true"/> iff the first two bytes of the buffer are a well-formed
        /// UTF-8 two-byte sequence. This wraps the mask check and the overlong check into a
        /// single operation. Returns <see langword="false"/> if running on a big-endian machine.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32BeginsWithValidUtf8TwoByteSequenceLittleEndian(uint value)
        {
            // Per Table 3-7, valid 2-byte sequences are [ C2..DF ] [ 80..BF ].
            // In little-endian, that would be represented as:
            // [ ######## ######## 10xxxxxx 110yyyyy ].
            // Due to the little-endian representation we can perform a trick by ANDing the low
            // WORD with the bitmask [ 11000000 11111111 ] and checking that the value is within
            // the range [ 10000000_11000010, 10000000_11011111 ]. This performs both the
            // 2-byte-sequence bitmask check and overlong form validation with one comparison.

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && UnicodeUtility.IsInRangeInclusive(value & 0xC0FFu, 0x80C2u, 0x80DFu))
                || (!BitConverter.IsLittleEndian && false);
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD on a little-endian machine,
        /// returns <see langword="true"/> iff the last two bytes of the buffer are a well-formed
        /// UTF-8 two-byte sequence. This wraps the mask check and the overlong check into a
        /// single operation. Returns <see langword="false"/> if running on a big-endian machine.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32EndsWithValidUtf8TwoByteSequenceLittleEndian(uint value)
        {
            // See comments in UInt32BeginsWithValidUtf8TwoByteSequenceLittleEndian.

            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && UnicodeUtility.IsInRangeInclusive(value & 0xC0FF_0000u, 0x80C2_0000u, 0x80DF_0000u))
                || (!BitConverter.IsLittleEndian && false);
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD in machine endianness,
        /// returns <see langword="true"/> iff the first byte of the buffer is ASCII.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32FirstByteIsAscii(uint value)
        {
            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && ((value & 0x80u) == 0))
                || (!BitConverter.IsLittleEndian && ((int)value >= 0));
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD in machine endianness,
        /// returns <see langword="true"/> iff the fourth byte of the buffer is ASCII.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32FourthByteIsAscii(uint value)
        {
            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && ((int)value >= 0))
                || (!BitConverter.IsLittleEndian && ((value & 0x80u) == 0));
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD in machine endianness,
        /// returns <see langword="true"/> iff the second byte of the buffer is ASCII.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32SecondByteIsAscii(uint value)
        {
            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && ((value & 0x8000u) == 0))
                || (!BitConverter.IsLittleEndian && ((value & 0x0080_0000u) == 0));
        }

        /// <summary>
        /// Given a UTF-8 buffer which has been read into a DWORD in machine endianness,
        /// returns <see langword="true"/> iff the third byte of the buffer is ASCII.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool UInt32ThirdByteIsAscii(uint value)
        {
            // Return statement is written this way to work around https://github.com/dotnet/runtime/issues/4207.

            return (BitConverter.IsLittleEndian && ((value & 0x0080_0000u) == 0))
                || (!BitConverter.IsLittleEndian && ((value & 0x8000u) == 0));
        }

        /// <summary>
        /// Given a DWORD which represents a buffer of 2 packed UTF-16 values in machine endianness,
        /// converts those scalar values to their 3-byte UTF-8 representation and writes the
        /// resulting 6 bytes to the destination buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteTwoUtf16CharsAsTwoUtf8ThreeByteSequences(ref byte outputBuffer, uint value)
        {
            if (BitConverter.IsLittleEndian)
            {
                // value = [ ZZZZYYYY YYXXXXXX zzzzyyyy yyxxxxxx ]
                // want to write [ 1110ZZZZ 10xxxxxx 10yyyyyy 1110zzzz ] [ 10XXXXXX 10YYYYYY ]

                uint tempA = ((value << 2) & 0x3F00u) | ((value & 0x3Fu) << 16); // = [ 00000000 00xxxxxx 00yyyyyy 00000000 ]
                uint tempB = ((value >> 4) & 0x0F00_0000u) | ((value >> 12) & 0x0Fu); // = [ 0000ZZZZ 00000000 00000000 0000zzzz ]
                Unsafe.WriteUnaligned(ref outputBuffer, tempA + tempB + 0xE080_80E0u); // = [ 1110ZZZZ 10xxxxxx 10yyyyyy 1110zzzz ]
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref outputBuffer, 4), (ushort)(((value >> 22) & 0x3Fu) + ((value >> 8) & 0x3F00u) + 0x8080u)); // = [ 10XXXXXX 10YYYYYY ]
            }
            else
            {
                // value = [ zzzzyyyy yyxxxxxx ZZZZYYYY YYXXXXXX ]
                // want to write [ 1110zzzz ] [ 10yyyyyy ] [ 10xxxxxx ] [ 1110ZZZZ ] [ 10YYYYYY ] [ 10XXXXXX ]

                Unsafe.Add(ref outputBuffer, 5) = (byte)((value & 0x3Fu) | 0x80u);
                Unsafe.Add(ref outputBuffer, 4) = (byte)(((value >>= 6) & 0x3Fu) | 0x80u);
                Unsafe.Add(ref outputBuffer, 3) = (byte)(((value >>= 6) & 0x0Fu) | 0xE0u);
                Unsafe.Add(ref outputBuffer, 2) = (byte)(((value >>= 4) & 0x3Fu) | 0x80u);
                Unsafe.Add(ref outputBuffer, 1) = (byte)(((value >>= 6) & 0x3Fu) | 0x80u);
                outputBuffer = (byte)((value >>= 6) | 0xE0u);
            }
        }

        /// <summary>
        /// Given a DWORD which represents a buffer of 2 packed UTF-16 values in machine endianness,
        /// converts the first UTF-16 value to its 3-byte UTF-8 representation and writes the
        /// resulting 3 bytes to the destination buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteFirstUtf16CharAsUtf8ThreeByteSequence(ref byte outputBuffer, uint value)
        {
            if (BitConverter.IsLittleEndian)
            {
                // value = [ ######## ######## zzzzyyyy yyxxxxxx ]
                // want to write [ 10yyyyyy 1110zzzz ] [ 10xxxxxx ]

                uint tempA = (value << 2) & 0x3F00u; // [ 00yyyyyy 00000000 ]
                uint tempB = ((uint)(ushort)value >> 12); // [ 00000000 0000zzzz ]
                Unsafe.WriteUnaligned(ref outputBuffer, (ushort)(tempA + tempB + 0x80E0u)); // [ 10yyyyyy 1110zzzz ]
                Unsafe.Add(ref outputBuffer, 2) = (byte)((value & 0x3Fu) | ~0x7Fu); // [ 10xxxxxx ]
            }
            else
            {
                // value = [ zzzzyyyy yyxxxxxx ######## ######## ]
                // want to write [ 1110zzzz ] [ 10yyyyyy ] [ 10xxxxxx ]

                Unsafe.Add(ref outputBuffer, 2) = (byte)(((value >>= 16) & 0x3Fu) | 0x80u);
                Unsafe.Add(ref outputBuffer, 1) = (byte)(((value >>= 6) & 0x3Fu) | 0x80u);
                outputBuffer = (byte)((value >>= 6) | 0xE0u);
            }
        }
    }
    internal static unsafe class Utf16Utility
    {
        /// <summary>
        /// Returns true iff the UInt32 represents two ASCII UTF-16 characters in machine endianness.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool AllCharsInUInt32AreAscii(uint value)
        {
            return (value & ~0x007F_007Fu) == 0;
        }

        /// <summary>
        /// Returns true iff the UInt64 represents four ASCII UTF-16 characters in machine endianness.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool AllCharsInUInt64AreAscii(ulong value)
        {
            return (value & ~0x007F_007F_007F_007Ful) == 0;
        }

        /// <summary>
        /// Given a UInt32 that represents two ASCII UTF-16 characters, returns the invariant
        /// lowercase representation of those characters. Requires the input value to contain
        /// two ASCII UTF-16 characters in machine endianness.
        /// </summary>
        /// <remarks>
        /// This is a branchless implementation.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint ConvertAllAsciiCharsInUInt32ToLowercase(uint value)
        {
            // ASSUMPTION: Caller has validated that input value is ASCII.

            // the 0x80 bit of each word of 'lowerIndicator' will be set iff the word has value >= 'A'
            uint lowerIndicator = value + 0x0080_0080u - 0x0041_0041u;

            // the 0x80 bit of each word of 'upperIndicator' will be set iff the word has value > 'Z'
            uint upperIndicator = value + 0x0080_0080u - 0x005B_005Bu;

            // the 0x80 bit of each word of 'combinedIndicator' will be set iff the word has value >= 'A' and <= 'Z'
            uint combinedIndicator = (lowerIndicator ^ upperIndicator);

            // the 0x20 bit of each word of 'mask' will be set iff the word has value >= 'A' and <= 'Z'
            uint mask = (combinedIndicator & 0x0080_0080u) >> 2;

            return value ^ mask; // bit flip uppercase letters [A-Z] => [a-z]
        }

        /// <summary>
        /// Given a UInt32 that represents two ASCII UTF-16 characters, returns the invariant
        /// uppercase representation of those characters. Requires the input value to contain
        /// two ASCII UTF-16 characters in machine endianness.
        /// </summary>
        /// <remarks>
        /// This is a branchless implementation.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint ConvertAllAsciiCharsInUInt32ToUppercase(uint value)
        {
            // ASSUMPTION: Caller has validated that input value is ASCII.

            // the 0x80 bit of each word of 'lowerIndicator' will be set iff the word has value >= 'a'
            uint lowerIndicator = value + 0x0080_0080u - 0x0061_0061u;

            // the 0x80 bit of each word of 'upperIndicator' will be set iff the word has value > 'z'
            uint upperIndicator = value + 0x0080_0080u - 0x007B_007Bu;

            // the 0x80 bit of each word of 'combinedIndicator' will be set iff the word has value >= 'a' and <= 'z'
            uint combinedIndicator = (lowerIndicator ^ upperIndicator);

            // the 0x20 bit of each word of 'mask' will be set iff the word has value >= 'a' and <= 'z'
            uint mask = (combinedIndicator & 0x0080_0080u) >> 2;

            return value ^ mask; // bit flip lowercase letters [a-z] => [A-Z]
        }

        /// <summary>
        /// Given a UInt64 that represents four ASCII UTF-16 characters, returns the invariant
        /// uppercase representation of those characters. Requires the input value to contain
        /// four ASCII UTF-16 characters in machine endianness.
        /// </summary>
        /// <remarks>
        /// This is a branchless implementation.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong ConvertAllAsciiCharsInUInt64ToUppercase(ulong value)
        {
            // ASSUMPTION: Caller has validated that input value is ASCII.

            // the 0x80 bit of each word of 'lowerIndicator' will be set iff the word has value >= 'a'
            ulong lowerIndicator = value + 0x0080_0080_0080_0080ul - 0x0061_0061_0061_0061ul;

            // the 0x80 bit of each word of 'upperIndicator' will be set iff the word has value > 'z'
            ulong upperIndicator = value + 0x0080_0080_0080_0080ul - 0x007B_007B_007B_007Bul;

            // the 0x80 bit of each word of 'combinedIndicator' will be set iff the word has value >= 'a' and <= 'z'
            ulong combinedIndicator = (lowerIndicator ^ upperIndicator);

            // the 0x20 bit of each word of 'mask' will be set iff the word has value >= 'a' and <= 'z'
            ulong mask = (combinedIndicator & 0x0080_0080_0080_0080ul) >> 2;

            return value ^ mask; // bit flip lowercase letters [a-z] => [A-Z]
        }

        /// <summary>
        /// Given a UInt64 that represents four ASCII UTF-16 characters, returns the invariant
        /// lowercase representation of those characters. Requires the input value to contain
        /// four ASCII UTF-16 characters in machine endianness.
        /// </summary>
        /// <remarks>
        /// This is a branchless implementation.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong ConvertAllAsciiCharsInUInt64ToLowercase(ulong value)
        {
            // ASSUMPTION: Caller has validated that input value is ASCII.

            // the 0x80 bit of each word of 'lowerIndicator' will be set iff the word has value >= 'A'
            ulong lowerIndicator = value + 0x0080_0080_0080_0080ul - 0x0041_0041_0041_0041ul;

            // the 0x80 bit of each word of 'upperIndicator' will be set iff the word has value > 'Z'
            ulong upperIndicator = value + 0x0080_0080_0080_0080ul - 0x005B_005B_005B_005Bul;

            // the 0x80 bit of each word of 'combinedIndicator' will be set iff the word has value >= 'a' and <= 'z'
            ulong combinedIndicator = (lowerIndicator ^ upperIndicator);

            // the 0x20 bit of each word of 'mask' will be set iff the word has value >= 'a' and <= 'z'
            ulong mask = (combinedIndicator & 0x0080_0080_0080_0080ul) >> 2;

            return value ^ mask; // bit flip uppercase letters [A-Z] => [a-z]
        }

        /// <summary>
        /// Given a UInt32 that represents two ASCII UTF-16 characters, returns true iff
        /// the input contains one or more lowercase ASCII characters.
        /// </summary>
        /// <remarks>
        /// This is a branchless implementation.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool UInt32ContainsAnyLowercaseAsciiChar(uint value)
        {
            // ASSUMPTION: Caller has validated that input value is ASCII.

            // the 0x80 bit of each word of 'lowerIndicator' will be set iff the word has value >= 'a'
            uint lowerIndicator = value + 0x0080_0080u - 0x0061_0061u;

            // the 0x80 bit of each word of 'upperIndicator' will be set iff the word has value > 'z'
            uint upperIndicator = value + 0x0080_0080u - 0x007B_007Bu;

            // the 0x80 bit of each word of 'combinedIndicator' will be set iff the word has value >= 'a' and <= 'z'
            uint combinedIndicator = (lowerIndicator ^ upperIndicator);

            return (combinedIndicator & 0x0080_0080u) != 0;
        }

        /// <summary>
        /// Given a UInt32 that represents two ASCII UTF-16 characters, returns true iff
        /// the input contains one or more uppercase ASCII characters.
        /// </summary>
        /// <remarks>
        /// This is a branchless implementation.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool UInt32ContainsAnyUppercaseAsciiChar(uint value)
        {
            // ASSUMPTION: Caller has validated that input value is ASCII.

            // the 0x80 bit of each word of 'lowerIndicator' will be set iff the word has value >= 'A'
            uint lowerIndicator = value + 0x0080_0080u - 0x0041_0041u;

            // the 0x80 bit of each word of 'upperIndicator' will be set iff the word has value > 'Z'
            uint upperIndicator = value + 0x0080_0080u - 0x005B_005Bu;

            // the 0x80 bit of each word of 'combinedIndicator' will be set iff the word has value >= 'A' and <= 'Z'
            uint combinedIndicator = (lowerIndicator ^ upperIndicator);

            return (combinedIndicator & 0x0080_0080u) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool UInt32OrdinalIgnoreCaseAscii(uint valueA, uint valueB)
        {
            // ASSUMPTION: Caller has validated that input values are ASCII.

            // Generate a mask of all bits which are different between A and B. Since [A-Z]
            // and [a-z] differ by the 0x20 bit, we'll left-shift this by 2 now so that
            // this is moved over to the 0x80 bit, which nicely aligns with the calculation
            // we're going to do on the indicator flag later.
            //
            // n.b. All of the logic below assumes we have at least 2 "known zero" bits leading
            // each of the 7-bit ASCII values. This assumption won't hold if this method is
            // ever adapted to deal with packed bytes instead of packed chars.

            uint differentBits = (valueA ^ valueB) << 2;

            // Now, we want to generate a mask where for each word in the input, the mask contains
            // 0xFF7F if the word is [A-Za-z], 0xFFFF if the word is not [A-Za-z]. We know each
            // input word is ASCII (only low 7 bit set), so we can use a combination of addition
            // and logical operators as follows.
            //
            // original input   +05         |A0         +1A
            // ====================================================
            //         00 .. 3F -> 05 .. 44 -> A5 .. E4 -> BF .. FE
            //               40 ->       45 ->       E5 ->       FF
            // ([A-Z]) 41 .. 5A -> 46 .. 5F -> E6 .. FF -> 00 .. 19
            //         5B .. 5F -> 60 .. 64 -> E0 .. E4 -> FA .. FE
            //               60 ->       65 ->       E5 ->       FF
            // ([a-z]) 61 .. 7A -> 66 .. 7F -> E6 .. FF -> 00 .. 19
            //         7B .. 7F -> 80 .. 84 -> A0 .. A4 -> BA .. BE
            //
            // This combination of operations results in the 0x80 bit of each word being set
            // iff the original word value was *not* [A-Za-z].

            uint indicator = valueA + 0x0005_0005u;
            indicator |= 0x00A0_00A0u;
            indicator += 0x001A_001Au;
            indicator |= 0xFF7F_FF7Fu; // normalize each word to 0xFF7F or 0xFFFF

            // At this point, 'indicator' contains the mask of bits which are *not* allowed to
            // differ between the inputs, and 'differentBits' contains the mask of bits which
            // actually differ between the inputs. If these masks have any bits in common, then
            // the two values are *not* equal under an OrdinalIgnoreCase comparer.

            return (differentBits & indicator) == 0;
        }

        /// <summary>
        /// Given two UInt64s that represent four ASCII UTF-16 characters each, returns true iff
        /// the two inputs are equal using an ordinal case-insensitive comparison.
        /// </summary>
        /// <remarks>
        /// This is a branchless implementation.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool UInt64OrdinalIgnoreCaseAscii(ulong valueA, ulong valueB)
        {
            // ASSUMPTION: Caller has validated that input values are ASCII.

            // Duplicate of logic in UInt32OrdinalIgnoreCaseAscii, but using 64-bit consts.
            // See comments in that method for more info.

            ulong differentBits = (valueA ^ valueB) << 2;
            ulong indicator = valueA + 0x0005_0005_0005_0005ul;
            indicator |= 0x00A0_00A0_00A0_00A0ul;
            indicator += 0x001A_001A_001A_001Aul;
            indicator |= 0xFF7F_FF7F_FF7F_FF7Ful;
            return (differentBits & indicator) == 0;
        }
    }
}