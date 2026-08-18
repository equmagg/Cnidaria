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

    public static class Ascii
    {
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
                if (!System.Globalization.UnicodeUtility.IsAsciiCodePoint(element))
                {
                    break;
                }

                if (conversionIsToUpper)
                {
                    if (System.Globalization.UnicodeUtility.IsInRangeInclusive(element, 'a', 'z'))
                    {
                        element -= 0x20u; // lowercase to uppercase
                    }
                }
                else
                {
                    if (System.Globalization.UnicodeUtility.IsInRangeInclusive(element, 'A', 'Z'))
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
    }
}

namespace System.Text.Unicode
{
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