using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.Wasm;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Unicode;

namespace System.Numerics
{
    /// <summary>Defines a mechanism for computing the sum of two values.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    /// <typeparam name="TOther">The type that will be added to <typeparamref name="TSelf" />.</typeparam>
    /// <typeparam name="TResult">The type that contains the sum of <typeparamref name="TSelf" /> and <typeparamref name="TOther" />.</typeparam>
    public interface IAdditionOperators<TSelf, TOther, TResult>
        where TSelf : IAdditionOperators<TSelf, TOther, TResult>?
    {
        /// <summary>Adds two values together to compute their sum.</summary>
        /// <param name="left">The value to which <paramref name="right" /> is added.</param>
        /// <param name="right">The value which is added to <paramref name="left" />.</param>
        /// <returns>The sum of <paramref name="left" /> and <paramref name="right" />.</returns>
        static abstract TResult operator +(TSelf left, TOther right);

        /// <summary>Adds two values together to compute their sum.</summary>
        /// <param name="left">The value to which <paramref name="right" /> is added.</param>
        /// <param name="right">The value which is added to <paramref name="left" />.</param>
        /// <returns>The sum of <paramref name="left" /> and <paramref name="right" />.</returns>
        /// <exception cref="OverflowException">The sum of <paramref name="left" /> and <paramref name="right" /> is not representable by <typeparamref name="TResult" />.</exception>
        static virtual TResult operator checked +(TSelf left, TOther right) => left + right;
    }
    /// <summary>Defines a mechanism for getting the additive identity of a given type.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    /// <typeparam name="TResult">The type that contains the additive identify of <typeparamref name="TSelf" />.</typeparam>
    public interface IAdditiveIdentity<TSelf, TResult>
        where TSelf : IAdditiveIdentity<TSelf, TResult>?
    {
        /// <summary>Gets the additive identity of the current type.</summary>
        static abstract TResult AdditiveIdentity { get; }
    }
    /// <summary>Defines an IEEE 754 floating-point type that is represented in a base-2 format.</summary>
    /// <typeparam name="TSelf">The type that implements the interface.</typeparam>
    public interface IBinaryFloatingPointIeee754<TSelf>
        : IBinaryNumber<TSelf>,
          IFloatingPointIeee754<TSelf>
        where TSelf : IBinaryFloatingPointIeee754<TSelf>?
    {
    }
    /// <summary>Defines an integer type that is represented in a base-2 format.</summary>
    /// <typeparam name="TSelf">The type that implements the interface.</typeparam>
    public interface IBinaryInteger<TSelf>
        : IBinaryNumber<TSelf>,
          IShiftOperators<TSelf, int, TSelf>
        where TSelf : IBinaryInteger<TSelf>?
    {
        /// <summary>Computes the quotient and remainder of two values.</summary>
        /// <param name="left">The value which <paramref name="right" /> divides.</param>
        /// <param name="right">The value which divides <paramref name="left" />.</param>
        /// <returns>The quotient and remainder of <paramref name="left" /> divided-by <paramref name="right" />.</returns>
        static virtual (TSelf Quotient, TSelf Remainder) DivRem(TSelf left, TSelf right)
        {
            TSelf quotient = left / right;
            return (quotient, (left - (quotient * right)));
        }

        /// <summary>Computes the number of leading zero bits in a value.</summary>
        /// <param name="value">The value whose leading zero bits are to be counted.</param>
        /// <returns>The number of leading zero bits in <paramref name="value" />.</returns>
        static virtual TSelf LeadingZeroCount(TSelf value)
        {
            if (!typeof(TSelf).IsValueType)
            {
                ArgumentNullException.ThrowIfNull(value);
            }

            TSelf bitCount = TSelf.CreateChecked(value!.GetByteCount() * 8L);

            if (value == TSelf.Zero)
            {
                return TSelf.CreateChecked(bitCount);
            }

            return TSelf.IsNegative(value) ? TSelf.Zero : ((bitCount - TSelf.One) ^ TSelf.Log2(value));
        }

        /// <summary>Computes the number of bits that are set in a value.</summary>
        /// <param name="value">The value whose set bits are to be counted.</param>
        /// <returns>The number of set bits in <paramref name="value" />.</returns>
        static abstract TSelf PopCount(TSelf value);

        /// <summary>Reads a two's complement number from a given array, in big-endian format, and converts it to an instance of the current type.</summary>
        /// <param name="source">The array from which the two's complement number should be read.</param>
        /// <param name="isUnsigned"><c>true</c> if <paramref name="source" /> represents an unsigned two's complement number; otherwise, <c>false</c> to indicate it represents a signed two's complement number.</param>
        /// <returns>The value read from <paramref name="source" />.</returns>
        /// <exception cref="OverflowException"><paramref name="source" /> is not representable by <typeparamref name="TSelf" /></exception>
        static virtual TSelf ReadBigEndian(byte[] source, bool isUnsigned)
        {
            if (!TSelf.TryReadBigEndian(source, isUnsigned, out TSelf value))
            {
                ThrowHelper.ThrowOverflowException();
            }
            return value;
        }

        /// <summary>Reads a two's complement number from a given array, in big-endian format, and converts it to an instance of the current type.</summary>
        /// <param name="source">The array from which the two's complement number should be read.</param>
        /// <param name="startIndex">The starting index from which the value should be read.</param>
        /// <param name="isUnsigned"><c>true</c> if <paramref name="source" /> represents an unsigned two's complement number; otherwise, <c>false</c> to indicate it represents a signed two's complement number.</param>
        /// <returns>The value read from <paramref name="source" /> starting at <paramref name="startIndex" />.</returns>
        /// <exception cref="OverflowException"><paramref name="source" /> is not representable by <typeparamref name="TSelf" /></exception>
        static virtual TSelf ReadBigEndian(byte[] source, int startIndex, bool isUnsigned)
        {
            if (!TSelf.TryReadBigEndian(source.AsSpan(startIndex), isUnsigned, out TSelf value))
            {
                ThrowHelper.ThrowOverflowException();
            }
            return value;
        }

        /// <summary>Reads a two's complement number from a given span, in big-endian format, and converts it to an instance of the current type.</summary>
        /// <param name="source">The array from which the two's complement number should be read.</param>
        /// <param name="isUnsigned"><c>true</c> if <paramref name="source" /> represents an unsigned two's complement number; otherwise, <c>false</c> to indicate it represents a signed two's complement number.</param>
        /// <returns>The value read from <paramref name="source" />.</returns>
        /// <exception cref="OverflowException"><paramref name="source" /> is not representable by <typeparamref name="TSelf" /></exception>
        static virtual TSelf ReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned)
        {
            if (!TSelf.TryReadBigEndian(source, isUnsigned, out TSelf value))
            {
                ThrowHelper.ThrowOverflowException();
            }
            return value;
        }

        /// <summary>Reads a two's complement number from a given array, in little-endian format, and converts it to an instance of the current type.</summary>
        /// <param name="source">The array from which the two's complement number should be read.</param>
        /// <param name="isUnsigned"><c>true</c> if <paramref name="source" /> represents an unsigned two's complement number; otherwise, <c>false</c> to indicate it represents a signed two's complement number.</param>
        /// <returns>The value read from <paramref name="source" />.</returns>
        /// <exception cref="OverflowException"><paramref name="source" /> is not representable by <typeparamref name="TSelf" /></exception>
        static virtual TSelf ReadLittleEndian(byte[] source, bool isUnsigned)
        {
            if (!TSelf.TryReadLittleEndian(source, isUnsigned, out TSelf value))
            {
                ThrowHelper.ThrowOverflowException();
            }
            return value;
        }

        /// <summary>Reads a two's complement number from a given array, in little-endian format, and converts it to an instance of the current type.</summary>
        /// <param name="source">The array from which the two's complement number should be read.</param>
        /// <param name="startIndex">The starting index from which the value should be read.</param>
        /// <param name="isUnsigned"><c>true</c> if <paramref name="source" /> represents an unsigned two's complement number; otherwise, <c>false</c> to indicate it represents a signed two's complement number.</param>
        /// <returns>The value read from <paramref name="source" /> starting at <paramref name="startIndex" />.</returns>
        /// <exception cref="OverflowException"><paramref name="source" /> is not representable by <typeparamref name="TSelf" /></exception>
        static virtual TSelf ReadLittleEndian(byte[] source, int startIndex, bool isUnsigned)
        {
            if (!TSelf.TryReadLittleEndian(source.AsSpan(startIndex), isUnsigned, out TSelf value))
            {
                ThrowHelper.ThrowOverflowException();
            }
            return value;
        }

        /// <summary>Reads a two's complement number from a given span, in little-endian format, and converts it to an instance of the current type.</summary>
        /// <param name="source">The array from which the two's complement number should be read.</param>
        /// <param name="isUnsigned"><c>true</c> if <paramref name="source" /> represents an unsigned two's complement number; otherwise, <c>false</c> to indicate it represents a signed two's complement number.</param>
        /// <returns>The value read from <paramref name="source" />.</returns>
        /// <exception cref="OverflowException"><paramref name="source" /> is not representable by <typeparamref name="TSelf" /></exception>
        static virtual TSelf ReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned)
        {
            if (!TSelf.TryReadLittleEndian(source, isUnsigned, out TSelf value))
            {
                ThrowHelper.ThrowOverflowException();
            }
            return value;
        }

        /// <summary>Rotates a value left by a given amount.</summary>
        /// <param name="value">The value which is rotated left by <paramref name="rotateAmount" />.</param>
        /// <param name="rotateAmount">The amount by which <paramref name="value" /> is rotated left.</param>
        /// <returns>The result of rotating <paramref name="value" /> left by <paramref name="rotateAmount" />.</returns>
        static virtual TSelf RotateLeft(TSelf value, int rotateAmount)
        {
            if (!typeof(TSelf).IsValueType)
            {
                ArgumentNullException.ThrowIfNull(value);
            }

            int bitCount = checked(value!.GetByteCount() * 8);
            return (value << rotateAmount) | (value >>> (bitCount - rotateAmount));
        }

        /// <summary>Rotates a value right by a given amount.</summary>
        /// <param name="value">The value which is rotated right by <paramref name="rotateAmount" />.</param>
        /// <param name="rotateAmount">The amount by which <paramref name="value" /> is rotated right.</param>
        /// <returns>The result of rotating <paramref name="value" /> right by <paramref name="rotateAmount" />.</returns>
        static virtual TSelf RotateRight(TSelf value, int rotateAmount)
        {
            if (!typeof(TSelf).IsValueType)
            {
                ArgumentNullException.ThrowIfNull(value);
            }

            int bitCount = checked(value!.GetByteCount() * 8);
            return (value >>> rotateAmount) | (value << (bitCount - rotateAmount));
        }

        /// <summary>Computes the number of trailing zero bits in a value.</summary>
        /// <param name="value">The value whose trailing zero bits are to be counted.</param>
        /// <returns>The number of trailing zero bits in <paramref name="value" />.</returns>
        static abstract TSelf TrailingZeroCount(TSelf value);

        /// <summary>Tries to read a two's complement number from a span, in big-endian format, and convert it to an instance of the current type.</summary>
        /// <param name="source">The span from which the two's complement number should be read.</param>
        /// <param name="isUnsigned"><c>true</c> if <paramref name="source" /> represents an unsigned two's complement number; otherwise, <c>false</c> to indicate it represents a signed two's complement number.</param>
        /// <param name="value">On return, contains the value read from <paramref name="source" /> or <c>default</c> if a value could not be read.</param>
        /// <returns><c>true</c> if the value was succesfully read from <paramref name="source" />; otherwise, <c>false</c>.</returns>
        static abstract bool TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out TSelf value);

        /// <summary>Tries to read a two's complement number from a span, in little-endian format, and convert it to an instance of the current type.</summary>
        /// <param name="source">The span from which the two's complement number should be read.</param>
        /// <param name="isUnsigned"><c>true</c> if <paramref name="source" /> represents an unsigned two's complement number; otherwise, <c>false</c> to indicate it represents a signed two's complement number.</param>
        /// <param name="value">On return, contains the value read from <paramref name="source" /> or <c>default</c> if a value could not be read.</param>
        /// <returns><c>true</c> if the value was succesfully read from <paramref name="source" />; otherwise, <c>false</c>.</returns>
        static abstract bool TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out TSelf value);

        /// <summary>Gets the number of bytes that will be written as part of <see cref="TryWriteLittleEndian(Span{byte}, out int)" />.</summary>
        /// <returns>The number of bytes that will be written as part of <see cref="TryWriteLittleEndian(Span{byte}, out int)" />.</returns>
        int GetByteCount();

        /// <summary>Gets the length, in bits, of the shortest two's complement representation of the current value.</summary>
        /// <returns>The length, in bits, of the shortest two's complement representation of the current value.</returns>
        int GetShortestBitLength();

        /// <summary>Tries to write the current value, in big-endian format, to a given span.</summary>
        /// <param name="destination">The span to which the current value should be written.</param>
        /// <param name="bytesWritten">The number of bytes written to <paramref name="destination" />.</param>
        /// <returns><c>true</c> if the value was successfully written to <paramref name="destination" />; otherwise, <c>false</c>.</returns>
        bool TryWriteBigEndian(Span<byte> destination, out int bytesWritten);

        /// <summary>Tries to write the current value, in little-endian format, to a given span.</summary>
        /// <param name="destination">The span to which the current value should be written.</param>
        /// <param name="bytesWritten">The number of bytes written to <paramref name="destination" />.</param>
        /// <returns><c>true</c> if the value was successfully written to <paramref name="destination" />; otherwise, <c>false</c>.</returns>
        bool TryWriteLittleEndian(Span<byte> destination, out int bytesWritten);

        /// <summary>Writes the current value, in big-endian format, to a given array.</summary>
        /// <param name="destination">The array to which the current value should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" />.</returns>
        int WriteBigEndian(byte[] destination)
        {
            if (!TryWriteBigEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current value, in big-endian format, to a given array.</summary>
        /// <param name="destination">The array to which the current value should be written.</param>
        /// <param name="startIndex">The starting index at which the value should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" /> starting at <paramref name="startIndex" />.</returns>
        int WriteBigEndian(byte[] destination, int startIndex)
        {
            if (!TryWriteBigEndian(destination.AsSpan(startIndex), out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current value, in big-endian format, to a given span.</summary>
        /// <param name="destination">The span to which the current value should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" />.</returns>
        int WriteBigEndian(Span<byte> destination)
        {
            if (!TryWriteBigEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current value, in little-endian format, to a given array.</summary>
        /// <param name="destination">The array to which the current value should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" />.</returns>
        int WriteLittleEndian(byte[] destination)
        {
            if (!TryWriteLittleEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current value, in little-endian format, to a given array.</summary>
        /// <param name="destination">The array to which the current value should be written.</param>
        /// <param name="startIndex">The starting index at which the value should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" /> starting at <paramref name="startIndex" />.</returns>
        int WriteLittleEndian(byte[] destination, int startIndex)
        {
            if (!TryWriteLittleEndian(destination.AsSpan(startIndex), out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current value, in little-endian format, to a given span.</summary>
        /// <param name="destination">The span to which the current value should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" />.</returns>
        int WriteLittleEndian(Span<byte> destination)
        {
            if (!TryWriteLittleEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }
    }
    /// <summary>Defines a number that is represented in a base-2 format.</summary>
    /// <typeparam name="TSelf">The type that implements the interface.</typeparam>
    public interface IBinaryNumber<TSelf>
        : IBitwiseOperators<TSelf, TSelf, TSelf>,
          INumber<TSelf>
        where TSelf : IBinaryNumber<TSelf>?
    {
        /// <summary>Gets an instance of the binary type in which all bits are set.</summary>
        static virtual TSelf AllBitsSet => ~TSelf.Zero;

        /// <summary>Determines if a value is a power of two.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is a power of two; otherwise, <c>false</c>.</returns>
        static abstract bool IsPow2(TSelf value);

        /// <summary>Computes the log2 of a value.</summary>
        /// <param name="value">The value whose log2 is to be computed.</param>
        /// <returns>The log2 of <paramref name="value" />.</returns>
        static abstract TSelf Log2(TSelf value);
    }
    /// <summary>Defines a mechanism for performing bitwise operations over two values.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    /// <typeparam name="TOther">The type that will is used in the operation with <typeparamref name="TSelf" />.</typeparam>
    /// <typeparam name="TResult">The type that contains the result of <typeparamref name="TSelf" /> op <typeparamref name="TOther" />.</typeparam>
    public interface IBitwiseOperators<TSelf, TOther, TResult>
        where TSelf : IBitwiseOperators<TSelf, TOther, TResult>?
    {
        /// <summary>Computes the bitwise-and of two values.</summary>
        /// <param name="left">The value to and with <paramref name="right" />.</param>
        /// <param name="right">The value to and with <paramref name="left" />.</param>
        /// <returns>The bitwise-and of <paramref name="left" /> and <paramref name="right" />.</returns>
        static abstract TResult operator &(TSelf left, TOther right);

        /// <summary>Computes the bitwise-or of two values.</summary>
        /// <param name="left">The value to or with <paramref name="right" />.</param>
        /// <param name="right">The value to or with <paramref name="left" />.</param>
        /// <returns>The bitwise-or of <paramref name="left" /> and <paramref name="right" />.</returns>
        static abstract TResult operator |(TSelf left, TOther right);

        /// <summary>Computes the exclusive-or of two values.</summary>
        /// <param name="left">The value to xor with <paramref name="right" />.</param>
        /// <param name="right">The value to xorwith <paramref name="left" />.</param>
        /// <returns>The exclusive-or of <paramref name="left" /> and <paramref name="right" />.</returns>
        static abstract TResult operator ^(TSelf left, TOther right);

        /// <summary>Computes the ones-complement representation of a given value.</summary>
        /// <param name="value">The value for which to compute its ones-complement.</param>
        /// <returns>The ones-complement of <paramref name="value" />.</returns>
        static abstract TResult operator ~(TSelf value);
    }
    /// <summary>Defines a mechanism for comparing two values to determine relative order.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    /// <typeparam name="TOther">The type that will be compared with <typeparamref name="TSelf" />.</typeparam>
    /// <typeparam name="TResult">The type that is returned as a result of the comparison.</typeparam>
    public interface IComparisonOperators<TSelf, TOther, TResult>
        : IEqualityOperators<TSelf, TOther, TResult>
        where TSelf : IComparisonOperators<TSelf, TOther, TResult>?
    {
        /// <summary>Compares two values to determine which is less.</summary>
        /// <param name="left">The value to compare with <paramref name="right" />.</param>
        /// <param name="right">The value to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if <paramref name="left" /> is less than <paramref name="right" />; otherwise, <c>false</c>.</returns>
        static abstract TResult operator <(TSelf left, TOther right);

        /// <summary>Compares two values to determine which is less or equal.</summary>
        /// <param name="left">The value to compare with <paramref name="right" />.</param>
        /// <param name="right">The value to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if <paramref name="left" /> is less than or equal to <paramref name="right" />; otherwise, <c>false</c>.</returns>
        static abstract TResult operator <=(TSelf left, TOther right);

        /// <summary>Compares two values to determine which is greater.</summary>
        /// <param name="left">The value to compare with <paramref name="right" />.</param>
        /// <param name="right">The value to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if <paramref name="left" /> is greater than <paramref name="right" />; otherwise, <c>false</c>.</returns>
        static abstract TResult operator >(TSelf left, TOther right);

        /// <summary>Compares two values to determine which is greater or equal.</summary>
        /// <param name="left">The value to compare with <paramref name="right" />.</param>
        /// <param name="right">The value to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if <paramref name="left" /> is greater than or equal to <paramref name="right" />; otherwise, <c>false</c>.</returns>
        static abstract TResult operator >=(TSelf left, TOther right);
    }
    /// <summary>Defines a mechanism for decrementing a given value.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    public interface IDecrementOperators<TSelf>
        where TSelf : IDecrementOperators<TSelf>?
    {
        /// <summary>Decrements a value.</summary>
        /// <param name="value">The value to decrement.</param>
        /// <returns>The result of decrementing <paramref name="value" />.</returns>
        static abstract TSelf operator --(TSelf value);

        /// <summary>Decrements a value.</summary>
        /// <param name="value">The value to decrement.</param>
        /// <returns>The result of decrementing <paramref name="value" />.</returns>
        /// <exception cref="OverflowException">The result of decrementing <paramref name="value" /> is not representable by <typeparamref name="TSelf" />.</exception>
        static virtual TSelf operator checked --(TSelf value) => --value;
    }
    /// <summary>Defines a mechanism for computing the quotient of two values.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    /// <typeparam name="TOther">The type that will divide <typeparamref name="TSelf" />.</typeparam>
    /// <typeparam name="TResult">The type that contains the quotient of <typeparamref name="TSelf" /> and <typeparamref name="TOther" />.</typeparam>
    public interface IDivisionOperators<TSelf, TOther, TResult>
        where TSelf : IDivisionOperators<TSelf, TOther, TResult>?
    {
        /// <summary>Divides two values together to compute their quotient.</summary>
        /// <param name="left">The value which <paramref name="right" /> divides.</param>
        /// <param name="right">The value which divides <paramref name="left" />.</param>
        /// <returns>The quotient of <paramref name="left" /> divided-by <paramref name="right" />.</returns>
        static abstract TResult operator /(TSelf left, TOther right);

        /// <summary>Divides two values together to compute their quotient.</summary>
        /// <param name="left">The value which <paramref name="right" /> divides.</param>
        /// <param name="right">The value which divides <paramref name="left" />.</param>
        /// <returns>The quotient of <paramref name="left" /> divided-by <paramref name="right" />.</returns>
        /// <exception cref="OverflowException">The quotient of <paramref name="left" /> divided-by <paramref name="right" /> is not representable by <typeparamref name="TResult" />.</exception>
        static virtual TResult operator checked /(TSelf left, TOther right) => left / right;
    }
    /// <summary>Defines a mechanism for comparing two values to determine equality.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    /// <typeparam name="TOther">The type that will be compared with <typeparamref name="TSelf" />.</typeparam>
    /// <typeparam name="TResult">The type that is returned as a result of the comparison.</typeparam>
    public interface IEqualityOperators<TSelf, TOther, TResult>
        where TSelf : IEqualityOperators<TSelf, TOther, TResult>?
    {
        /// <summary>Compares two values to determine equality.</summary>
        /// <param name="left">The value to compare with <paramref name="right" />.</param>
        /// <param name="right">The value to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if <paramref name="left" /> is equal to <paramref name="right" />; otherwise, <c>false</c>.</returns>
        static abstract TResult operator ==(TSelf? left, TOther? right);

        /// <summary>Compares two values to determine inequality.</summary>
        /// <param name="left">The value to compare with <paramref name="right" />.</param>
        /// <param name="right">The value to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if <paramref name="left" /> is not equal to <paramref name="right" />; otherwise, <c>false</c>.</returns>
        static abstract TResult operator !=(TSelf? left, TOther? right);
    }
    /// <summary>Defines support for exponential functions.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    public interface IExponentialFunctions<TSelf>
        : IFloatingPointConstants<TSelf>
        where TSelf : IExponentialFunctions<TSelf>?
    {
        /// <summary>Computes <c>E</c> raised to a given power.</summary>
        /// <param name="x">The power to which <c>E</c> is raised.</param>
        /// <returns><c>E<sup><paramref name="x" /></sup></c></returns>
        static abstract TSelf Exp(TSelf x);

        /// <summary>Computes <c>E</c> raised to a given power and subtracts one.</summary>
        /// <param name="x">The power to which <c>E</c> is raised.</param>
        /// <returns><c>E<sup><paramref name="x" /></sup> - 1</c></returns>
        static virtual TSelf ExpM1(TSelf x) => TSelf.Exp(x) - TSelf.One;

        /// <summary>Computes <c>2</c> raised to a given power.</summary>
        /// <param name="x">The power to which <c>2</c> is raised.</param>
        /// <returns><c>2<sup><paramref name="x" /></sup></c></returns>
        static abstract TSelf Exp2(TSelf x);

        /// <summary>Computes <c>2</c> raised to a given power and subtracts one.</summary>
        /// <param name="x">The power to which <c>2</c> is raised.</param>
        /// <returns><c>2<sup><paramref name="x" /></sup> - 1</c></returns>
        static virtual TSelf Exp2M1(TSelf x) => TSelf.Exp2(x) - TSelf.One;

        /// <summary>Computes <c>10</c> raised to a given power.</summary>
        /// <param name="x">The power to which <c>10</c> is raised.</param>
        /// <returns><c>10<sup><paramref name="x" /></sup></c></returns>
        static abstract TSelf Exp10(TSelf x);

        /// <summary>Computes <c>10</c> raised to a given power and subtracts one.</summary>
        /// <param name="x">The power to which <c>10</c> is raised.</param>
        /// <returns><c>10<sup><paramref name="x" /></sup> - 1</c></returns>
        static virtual TSelf Exp10M1(TSelf x) => TSelf.Exp10(x) - TSelf.One;
    }
    /// <summary>Defines a floating-point type.</summary>
    /// <typeparam name="TSelf">The type that implements the interface.</typeparam>
    public interface IFloatingPoint<TSelf>
        : IFloatingPointConstants<TSelf>,
          INumber<TSelf>,
          ISignedNumber<TSelf>
        where TSelf : IFloatingPoint<TSelf>?
    {
        /// <summary>Computes the ceiling of a value.</summary>
        /// <param name="x">The value whose ceiling is to be computed.</param>
        /// <returns>The ceiling of <paramref name="x" />.</returns>
        static virtual TSelf Ceiling(TSelf x) => TSelf.Round(x, digits: 0, MidpointRounding.ToPositiveInfinity);

        /// <summary>Converts a value to a specified integer type using saturation on overflow</summary>
        /// <typeparam name="TInteger">The integer type to which <paramref name="value" /> is converted.</typeparam>
        /// <param name="value">The value to be converted.</param>
        /// <returns>An instance of <typeparamref name="TInteger" /> created from <paramref name="value" />.</returns>
        static virtual TInteger ConvertToInteger<TInteger>(TSelf value)
            where TInteger : IBinaryInteger<TInteger>
        {
            return TInteger.CreateSaturating(value);
        }

        /// <summary>Converts a value to a specified integer type using platform specific behavior on overflow.</summary>
        /// <typeparam name="TInteger">The integer type to which <paramref name="value" /> is converted.</typeparam>
        /// <param name="value">The value to be converted.</param>
        /// <returns>An instance of <typeparamref name="TInteger" /> created from <paramref name="value" />.</returns>
        static virtual TInteger ConvertToIntegerNative<TInteger>(TSelf value)
            where TInteger : IBinaryInteger<TInteger>
        {
            return TSelf.ConvertToInteger<TInteger>(value);
        }

        /// <summary>Computes the floor of a value.</summary>
        /// <param name="x">The value whose floor is to be computed.</param>
        /// <returns>The floor of <paramref name="x" />.</returns>
        static virtual TSelf Floor(TSelf x) => TSelf.Round(x, digits: 0, MidpointRounding.ToNegativeInfinity);

        /// <summary>Rounds a value to the nearest integer using the default rounding mode (<see cref="MidpointRounding.ToEven" />).</summary>
        /// <param name="x">The value to round.</param>
        /// <returns>The result of rounding <paramref name="x" /> to the nearest integer using the default rounding mode.</returns>
        static virtual TSelf Round(TSelf x) => TSelf.Round(x, digits: 0, MidpointRounding.ToEven);

        /// <summary>Rounds a value to a specified number of fractional-digits using the default rounding mode (<see cref="MidpointRounding.ToEven" />).</summary>
        /// <param name="x">The value to round.</param>
        /// <param name="digits">The number of fractional digits to which <paramref name="x" /> should be rounded.</param>
        /// <returns>The result of rounding <paramref name="x" /> to <paramref name="digits" /> fractional-digits using the default rounding mode.</returns>
        static virtual TSelf Round(TSelf x, int digits) => TSelf.Round(x, digits, MidpointRounding.ToEven);

        /// <summary>Rounds a value to the nearest integer using the specified rounding mode.</summary>
        /// <param name="x">The value to round.</param>
        /// <param name="mode">The mode under which <paramref name="x" /> should be rounded.</param>
        /// <returns>The result of rounding <paramref name="x" /> to the nearest integer using <paramref name="mode" />.</returns>
        static virtual TSelf Round(TSelf x, MidpointRounding mode) => TSelf.Round(x, digits: 0, mode);

        /// <summary>Rounds a value to a specified number of fractional-digits using the specified rounding mode.</summary>
        /// <param name="x">The value to round.</param>
        /// <param name="digits">The number of fractional digits to which <paramref name="x" /> should be rounded.</param>
        /// <param name="mode">The mode under which <paramref name="x" /> should be rounded.</param>
        /// <returns>The result of rounding <paramref name="x" /> to <paramref name="digits" /> fractional-digits using <paramref name="mode" />.</returns>
        static abstract TSelf Round(TSelf x, int digits, MidpointRounding mode);

        /// <summary>Truncates a value.</summary>
        /// <param name="x">The value to truncate.</param>
        /// <returns>The truncation of <paramref name="x" />.</returns>
        static virtual TSelf Truncate(TSelf x) => TSelf.Round(x, digits: 0, MidpointRounding.ToZero);

        /// <summary>Gets the number of bytes that will be written as part of <see cref="TryWriteExponentLittleEndian(Span{byte}, out int)" />.</summary>
        /// <returns>The number of bytes that will be written as part of <see cref="TryWriteExponentLittleEndian(Span{byte}, out int)" />.</returns>
        int GetExponentByteCount();

        /// <summary>Gets the length, in bits, of the shortest two's complement representation of the current exponent.</summary>
        /// <returns>The length, in bits, of the shortest two's complement representation of the current exponent.</returns>
        int GetExponentShortestBitLength();

        /// <summary>Gets the length, in bits, of the current significand.</summary>
        /// <returns>The length, in bits, of the current significand.</returns>
        int GetSignificandBitLength();

        /// <summary>Gets the number of bytes that will be written as part of <see cref="TryWriteSignificandLittleEndian(Span{byte}, out int)" />.</summary>
        /// <returns>The number of bytes that will be written as part of <see cref="TryWriteSignificandLittleEndian(Span{byte}, out int)" />.</returns>
        int GetSignificandByteCount();

        /// <summary>Tries to write the current exponent, in big-endian format, to a given span.</summary>
        /// <param name="destination">The span to which the current exponent should be written.</param>
        /// <param name="bytesWritten">The number of bytes written to <paramref name="destination" />.</param>
        /// <returns><c>true</c> if the exponent was successfully written to <paramref name="destination" />; otherwise, <c>false</c>.</returns>
        bool TryWriteExponentBigEndian(Span<byte> destination, out int bytesWritten);

        /// <summary>Tries to write the current exponent, in little-endian format, to a given span.</summary>
        /// <param name="destination">The span to which the current exponent should be written.</param>
        /// <param name="bytesWritten">The number of bytes written to <paramref name="destination" />.</param>
        /// <returns><c>true</c> if the exponent was successfully written to <paramref name="destination" />; otherwise, <c>false</c>.</returns>
        bool TryWriteExponentLittleEndian(Span<byte> destination, out int bytesWritten);

        /// <summary>Tries to write the current significand, in big-endian format, to a given span.</summary>
        /// <param name="destination">The span to which the current significand should be written.</param>
        /// <param name="bytesWritten">The number of bytes written to <paramref name="destination" />.</param>
        /// <returns><c>true</c> if the significand was successfully written to <paramref name="destination" />; otherwise, <c>false</c>.</returns>
        bool TryWriteSignificandBigEndian(Span<byte> destination, out int bytesWritten);

        /// <summary>Tries to write the current significand, in little-endian format, to a given span.</summary>
        /// <param name="destination">The span to which the current significand should be written.</param>
        /// <param name="bytesWritten">The number of bytes written to <paramref name="destination" />.</param>
        /// <returns><c>true</c> if the significand was successfully written to <paramref name="destination" />; otherwise, <c>false</c>.</returns>
        bool TryWriteSignificandLittleEndian(Span<byte> destination, out int bytesWritten);

        /// <summary>Writes the current exponent, in big-endian format, to a given array.</summary>
        /// <param name="destination">The array to which the current exponent should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" />.</returns>
        int WriteExponentBigEndian(byte[] destination)
        {
            if (!TryWriteExponentBigEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current exponent, in big-endian format, to a given array.</summary>
        /// <param name="destination">The array to which the current exponent should be written.</param>
        /// <param name="startIndex">The starting index at which the exponent should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" /> starting at <paramref name="startIndex" />.</returns>
        int WriteExponentBigEndian(byte[] destination, int startIndex)
        {
            if (!TryWriteExponentBigEndian(destination.AsSpan(startIndex), out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current exponent, in big-endian format, to a given span.</summary>
        /// <param name="destination">The span to which the current exponent should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" />.</returns>
        int WriteExponentBigEndian(Span<byte> destination)
        {
            if (!TryWriteExponentBigEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current exponent, in little-endian format, to a given array.</summary>
        /// <param name="destination">The array to which the current exponent should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" />.</returns>
        int WriteExponentLittleEndian(byte[] destination)
        {
            if (!TryWriteExponentLittleEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current exponent, in little-endian format, to a given array.</summary>
        /// <param name="destination">The array to which the current exponent should be written.</param>
        /// <param name="startIndex">The starting index at which the exponent should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" /> starting at <paramref name="startIndex" />.</returns>
        int WriteExponentLittleEndian(byte[] destination, int startIndex)
        {
            if (!TryWriteExponentLittleEndian(destination.AsSpan(startIndex), out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current exponent, in little-endian format, to a given span.</summary>
        /// <param name="destination">The span to which the current exponent should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" />.</returns>
        int WriteExponentLittleEndian(Span<byte> destination)
        {
            if (!TryWriteExponentLittleEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current significand, in big-endian format, to a given array.</summary>
        /// <param name="destination">The array to which the current significand should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" />.</returns>
        int WriteSignificandBigEndian(byte[] destination)
        {
            if (!TryWriteSignificandBigEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current significand, in big-endian format, to a given array.</summary>
        /// <param name="destination">The array to which the current significand should be written.</param>
        /// <param name="startIndex">The starting index at which the significand should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" /> starting at <paramref name="startIndex" />.</returns>
        int WriteSignificandBigEndian(byte[] destination, int startIndex)
        {
            if (!TryWriteSignificandBigEndian(destination.AsSpan(startIndex), out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current significand, in big-endian format, to a given span.</summary>
        /// <param name="destination">The span to which the current significand should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" />.</returns>
        int WriteSignificandBigEndian(Span<byte> destination)
        {
            if (!TryWriteSignificandBigEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current significand, in little-endian format, to a given array.</summary>
        /// <param name="destination">The array to which the current significand should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" />.</returns>
        int WriteSignificandLittleEndian(byte[] destination)
        {
            if (!TryWriteSignificandLittleEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current significand, in little-endian format, to a given array.</summary>
        /// <param name="destination">The array to which the current significand should be written.</param>
        /// <param name="startIndex">The starting index at which the significand should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" /> starting at <paramref name="startIndex" />.</returns>
        int WriteSignificandLittleEndian(byte[] destination, int startIndex)
        {
            if (!TryWriteSignificandLittleEndian(destination.AsSpan(startIndex), out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }

        /// <summary>Writes the current significand, in little-endian format, to a given span.</summary>
        /// <param name="destination">The span to which the current significand should be written.</param>
        /// <returns>The number of bytes written to <paramref name="destination" />.</returns>
        int WriteSignificandLittleEndian(Span<byte> destination)
        {
            if (!TryWriteSignificandLittleEndian(destination, out int bytesWritten))
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
            return bytesWritten;
        }
    }
    /// <summary>Defines support for floating-point constants.</summary>
    /// <typeparam name="TSelf">The type that implements the interface.</typeparam>
    public interface IFloatingPointConstants<TSelf>
        : INumberBase<TSelf>
        where TSelf : IFloatingPointConstants<TSelf>?
    {
        /// <summary>Gets the mathematical constant <c>e</c>.</summary>
        static abstract TSelf E { get; }

        /// <summary>Gets the mathematical constant <c>pi</c>.</summary>
        static abstract TSelf Pi { get; }

        /// <summary>Gets the mathematical constant <c>tau</c>.</summary>
        static abstract TSelf Tau { get; }
    }
    /// <summary>Defines an IEEE 754 floating-point type.</summary>
    /// <typeparam name="TSelf">The type that implements the interface.</typeparam>
    public interface IFloatingPointIeee754<TSelf>
        : IExponentialFunctions<TSelf>,
          IFloatingPoint<TSelf>,
          IHyperbolicFunctions<TSelf>,
          ILogarithmicFunctions<TSelf>,
          IPowerFunctions<TSelf>,
          IRootFunctions<TSelf>,
          ITrigonometricFunctions<TSelf>
        where TSelf : IFloatingPointIeee754<TSelf>?
    {
        /// <summary>Gets the smallest value such that can be added to <c>0</c> that does not result in <c>0</c>.</summary>
        static abstract TSelf Epsilon { get; }

        /// <summary>Gets a value that represents <c>NaN</c>.</summary>
        static abstract TSelf NaN { get; }

        /// <summary>Gets a value that represents negative <c>infinity</c>.</summary>
        static abstract TSelf NegativeInfinity { get; }

        /// <summary>Gets a value that represents negative <c>zero</c>.</summary>
        static abstract TSelf NegativeZero { get; }

        /// <summary>Gets a value that represents positive <c>infinity</c>.</summary>
        static abstract TSelf PositiveInfinity { get; }

        /// <summary>Computes the arc-tangent for the quotient of two values.</summary>
        /// <param name="y">The y-coordinate of a point.</param>
        /// <param name="x">The x-coordinate of a point.</param>
        /// <returns>The arc-tangent of <paramref name="y" /> divided-by <paramref name="x" />.</returns>
        /// <remarks>This computes <c>arctan(y / x)</c> in the interval <c>[-PI, +PI]</c> radians.</remarks>
        static abstract TSelf Atan2(TSelf y, TSelf x);

        /// <summary>Computes the arc-tangent for the quotient of two values and divides the result by <c>pi</c>.</summary>
        /// <param name="y">The y-coordinate of a point.</param>
        /// <param name="x">The x-coordinate of a point.</param>
        /// <returns>The arc-tangent of <paramref name="y" /> divided-by <paramref name="x" />, divided by <c>pi</c>.</returns>
        /// <remarks>This computes <c>arctan(y / x) / PI</c> in the interval <c>[-1, +1]</c>.</remarks>
        static abstract TSelf Atan2Pi(TSelf y, TSelf x);

        /// <summary>Decrements a value to the largest value that compares less than a given value.</summary>
        /// <param name="x">The value to be bitwise decremented.</param>
        /// <returns>The largest value that compares less than <paramref name="x" />.</returns>
        static abstract TSelf BitDecrement(TSelf x);

        /// <summary>Increments a value to the smallest value that compares greater than a given value.</summary>
        /// <param name="x">The value to be bitwise incremented.</param>
        /// <returns>The smallest value that compares greater than <paramref name="x" />.</returns>
        static abstract TSelf BitIncrement(TSelf x);

        /// <summary>Computes the fused multiply-add of three values.</summary>
        /// <param name="left">The value which <paramref name="right" /> multiplies.</param>
        /// <param name="right">The value which multiplies <paramref name="left" />.</param>
        /// <param name="addend">The value that is added to the product of <paramref name="left" /> and <paramref name="right" />.</param>
        /// <returns>The result of <paramref name="left" /> times <paramref name="right" /> plus <paramref name="addend" /> computed as one ternary operation.</returns>
        static abstract TSelf FusedMultiplyAdd(TSelf left, TSelf right, TSelf addend);

        /// <summary>Computes the remainder of two values as specified by IEEE 754.</summary>
        /// <param name="left">The value which <paramref name="right" /> divides.</param>
        /// <param name="right">The value which divides <paramref name="left" />.</param>
        /// <returns>The remainder of <paramref name="left" /> divided-by <paramref name="right" /> as specified by IEEE 754.</returns>
        static abstract TSelf Ieee754Remainder(TSelf left, TSelf right);

        /// <summary>Computes the integer logarithm of a value.</summary>
        /// <param name="x">The value whose integer logarithm is to be computed.</param>
        /// <returns>The integer logarithm of <paramref name="x" />.</returns>
        static abstract int ILogB(TSelf x);

        /// <summary>Performs a linear interpolation between two values based on the given weight.</summary>
        /// <param name="value1">The first value, which is intended to be the lower bound.</param>
        /// <param name="value2">The second value, which is intended to be the upper bound.</param>
        /// <param name="amount">A value, intended to be between 0 and 1, that indicates the weight of the interpolation.</param>
        /// <returns>The interpolated value.</returns>
        /// <remarks>This method presumes inputs are well formed and does not validate that <c>value1 &lt; value2</c> nor that <c>0 &lt;= amount &lt;= 1</c>.</remarks>
        static virtual TSelf Lerp(TSelf value1, TSelf value2, TSelf amount) => TSelf.MultiplyAddEstimate(value1, TSelf.One - amount, value2 * amount);

        /// <summary>Computes an estimate of the reciprocal of a value.</summary>
        /// <param name="x">The value whose estimate of the reciprocal is to be computed.</param>
        /// <returns>An estimate of the reciprocal of <paramref name="x" />.</returns>
        static virtual TSelf ReciprocalEstimate(TSelf x) => TSelf.One / x;

        /// <summary>Computes an estimate of the reciprocal square root of a value.</summary>
        /// <param name="x">The value whose estimate of the reciprocal square root is to be computed.</param>
        /// <returns>An estimate of the reciprocal square root of <paramref name="x" />.</returns>
        static virtual TSelf ReciprocalSqrtEstimate(TSelf x) => TSelf.One / TSelf.Sqrt(x);

        /// <summary>Computes the product of a value and its base-radix raised to the specified power.</summary>
        /// <param name="x">The value which base-radix raised to the power of <paramref name="n" /> multiplies.</param>
        /// <param name="n">The value to which base-radix is raised before multipliying <paramref name="x" />.</param>
        /// <returns>The product of <paramref name="x" /> and base-radix raised to the power of <paramref name="n" />.</returns>
        static abstract TSelf ScaleB(TSelf x, int n);

        // The following methods are approved but not yet implemented in the libraries
        // * static abstract TSelf Compound(TSelf x, TSelf n);
    }
    /// <summary>Defines support for hyperbolic functions.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    public interface IHyperbolicFunctions<TSelf>
        : IFloatingPointConstants<TSelf>
        where TSelf : IHyperbolicFunctions<TSelf>?
    {
        /// <summary>Computes the hyperbolic arc-cosine of a value.</summary>
        /// <param name="x">The value, in radians, whose hyperbolic arc-cosine is to be computed.</param>
        /// <returns>The hyperbolic arc-cosine of <paramref name="x" />.</returns>
        static abstract TSelf Acosh(TSelf x);

        /// <summary>Computes the hyperbolic arc-sine of a value.</summary>
        /// <param name="x">The value, in radians, whose hyperbolic arc-sine is to be computed.</param>
        /// <returns>The hyperbolic arc-sine of <paramref name="x" />.</returns>
        static abstract TSelf Asinh(TSelf x);

        /// <summary>Computes the hyperbolic arc-tangent of a value.</summary>
        /// <param name="x">The value, in radians, whose hyperbolic arc-tangent is to be computed.</param>
        /// <returns>The hyperbolic arc-tangent of <paramref name="x" />.</returns>
        static abstract TSelf Atanh(TSelf x);

        /// <summary>Computes the hyperbolic cosine of a value.</summary>
        /// <param name="x">The value, in radians, whose hyperbolic cosine is to be computed.</param>
        /// <returns>The hyperbolic cosine of <paramref name="x" />.</returns>
        static abstract TSelf Cosh(TSelf x);

        /// <summary>Computes the hyperbolic sine of a value.</summary>
        /// <param name="x">The value, in radians, whose hyperbolic sine is to be computed.</param>
        /// <returns>The hyperbolic sine of <paramref name="x" />.</returns>
        static abstract TSelf Sinh(TSelf x);

        /// <summary>Computes the hyperbolic tangent of a value.</summary>
        /// <param name="x">The value, in radians, whose hyperbolic tangent is to be computed.</param>
        /// <returns>The hyperbolic tangent of <paramref name="x" />.</returns>
        static abstract TSelf Tanh(TSelf x);
    }
    /// <summary>Defines a mechanism for incrementing a given value.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    public interface IIncrementOperators<TSelf>
        where TSelf : IIncrementOperators<TSelf>?
    {
        /// <summary>Increments a value.</summary>
        /// <param name="value">The value to increment.</param>
        /// <returns>The result of incrementing <paramref name="value" />.</returns>
        static abstract TSelf operator ++(TSelf value);

        /// <summary>Increments a value.</summary>
        /// <param name="value">The value to increment.</param>
        /// <returns>The result of incrementing <paramref name="value" />.</returns>
        /// <exception cref="OverflowException">The result of incrementing <paramref name="value" /> is not representable by <typeparamref name="TSelf" />.</exception>
        static virtual TSelf operator checked ++(TSelf value) => ++value;
    }
    /// <summary>Defines support for logarithmic functions.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    public interface ILogarithmicFunctions<TSelf>
        : IFloatingPointConstants<TSelf>
        where TSelf : ILogarithmicFunctions<TSelf>?
    {
        /// <summary>Computes the natural (<c>base-E</c>) logarithm of a value.</summary>
        /// <param name="x">The value whose natural logarithm is to be computed.</param>
        /// <returns><c>log<sub>e</sub>(<paramref name="x" />)</c></returns>
        static abstract TSelf Log(TSelf x);

        /// <summary>Computes the logarithm of a value in the specified base.</summary>
        /// <param name="x">The value whose logarithm is to be computed.</param>
        /// <param name="newBase">The base in which the logarithm is to be computed.</param>
        /// <returns><c>log<sub><paramref name="newBase" /></sub>(<paramref name="x" />)</c></returns>
        static abstract TSelf Log(TSelf x, TSelf newBase);

        /// <summary>Computes the natural (<c>base-E</c>) logarithm of a value plus one.</summary>
        /// <param name="x">The value to which one is added before computing the natural logarithm.</param>
        /// <returns><c>log<sub>e</sub>(<paramref name="x" /> + 1)</c></returns>
        static virtual TSelf LogP1(TSelf x) => TSelf.Log(x + TSelf.One);

        /// <summary>Computes the base-2 logarithm of a value.</summary>
        /// <param name="x">The value whose base-2 logarithm is to be computed.</param>
        /// <returns><c>log<sub>2</sub>(<paramref name="x" />)</c></returns>
        static abstract TSelf Log2(TSelf x);

        /// <summary>Computes the base-2 logarithm of a value plus one.</summary>
        /// <param name="x">The value to which one is added before computing the base-2 logarithm.</param>
        /// <returns><c>log<sub>2</sub>(<paramref name="x" /> + 1)</c></returns>
        static virtual TSelf Log2P1(TSelf x) => TSelf.Log2(x + TSelf.One);

        /// <summary>Computes the base-10 logarithm of a value.</summary>
        /// <param name="x">The value whose base-10 logarithm is to be computed.</param>
        /// <returns><c>log<sub>10</sub>(<paramref name="x" />)</c></returns>
        static abstract TSelf Log10(TSelf x);

        /// <summary>Computes the base-10 logarithm of a value plus one.</summary>
        /// <param name="x">The value to which one is added before computing the base-10 logarithm.</param>
        /// <returns><c>log<sub>10</sub>(<paramref name="x" /> + 1)</c></returns>
        static virtual TSelf Log10P1(TSelf x) => TSelf.Log10(x + TSelf.One);
    }
    /// <summary>Defines a mechanism for getting the minimum and maximum value of a type.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    public interface IMinMaxValue<TSelf>
        where TSelf : IMinMaxValue<TSelf>?
    {
        /// <summary>Gets the minimum value of the current type.</summary>
        static abstract TSelf MinValue { get; }

        /// <summary>Gets the maximum value of the current type.</summary>
        static abstract TSelf MaxValue { get; }
    }
    /// <summary>Defines a mechanism for computing the modulus or remainder of two values.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    /// <typeparam name="TOther">The type that will divide <typeparamref name="TSelf" />.</typeparam>
    /// <typeparam name="TResult">The type that contains the modulus or remainder of <typeparamref name="TSelf" /> and <typeparamref name="TOther" />.</typeparam>
    /// <remarks>This type represents the <c>%</c> in C# which is often used to compute the remainder and may differ from an actual modulo operation depending on the type that implements the interface.</remarks>
    public interface IModulusOperators<TSelf, TOther, TResult>
        where TSelf : IModulusOperators<TSelf, TOther, TResult>?
    {
        /// <summary>Divides two values together to compute their modulus or remainder.</summary>
        /// <param name="left">The value which <paramref name="right" /> divides.</param>
        /// <param name="right">The value which divides <paramref name="left" />.</param>
        /// <returns>The modulus or remainder of <paramref name="left" /> divided-by <paramref name="right" />.</returns>
        static abstract TResult operator %(TSelf left, TOther right);
    }
    /// <summary>Defines a mechanism for getting the multiplicative identity of a given type.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    /// <typeparam name="TResult">The type that contains the multiplicative identify of <typeparamref name="TSelf" />.</typeparam>
    public interface IMultiplicativeIdentity<TSelf, TResult>
        where TSelf : IMultiplicativeIdentity<TSelf, TResult>?
    {
        /// <summary>Gets the multiplicative identity of the current type.</summary>
        static abstract TResult MultiplicativeIdentity { get; }
    }
    /// <summary>Defines a mechanism for computing the product of two values.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    /// <typeparam name="TOther">The type that will multiply <typeparamref name="TSelf" />.</typeparam>
    /// <typeparam name="TResult">The type that contains the product of <typeparamref name="TSelf" /> and <typeparamref name="TOther" />.</typeparam>
    public interface IMultiplyOperators<TSelf, TOther, TResult>
        where TSelf : IMultiplyOperators<TSelf, TOther, TResult>?
    {
        /// <summary>Multiplies two values together to compute their product.</summary>
        /// <param name="left">The value which <paramref name="right" /> multiplies.</param>
        /// <param name="right">The value which multiplies <paramref name="left" />.</param>
        /// <returns>The product of <paramref name="left" /> multiplied-by <paramref name="right" />.</returns>
        static abstract TResult operator *(TSelf left, TOther right);

        /// <summary>Multiplies two values together to compute their product.</summary>
        /// <param name="left">The value which <paramref name="right" /> multiplies.</param>
        /// <param name="right">The value which multiplies <paramref name="left" />.</param>
        /// <returns>The product of <paramref name="left" /> multiplied-by <paramref name="right" />.</returns>
        /// <exception cref="OverflowException">The product of <paramref name="left" /> multiplied-by <paramref name="right" /> is not representable by <typeparamref name="TResult" />.</exception>
        static virtual TResult operator checked *(TSelf left, TOther right) => left * right;
    }
    /// <summary>Defines a number type.</summary>
    /// <typeparam name="TSelf">The type that implements the interface.</typeparam>
    public interface INumber<TSelf>
        : IComparable,
          IComparable<TSelf>,
          IComparisonOperators<TSelf, TSelf, bool>,
          IModulusOperators<TSelf, TSelf, TSelf>,
          INumberBase<TSelf>
        where TSelf : INumber<TSelf>?
    {
        /// <summary>Clamps a value to an inclusive minimum and maximum value.</summary>
        /// <param name="value">The value to clamp.</param>
        /// <param name="min">The inclusive minimum to which <paramref name="value" /> should clamp.</param>
        /// <param name="max">The inclusive maximum to which <paramref name="value" /> should clamp.</param>
        /// <returns>The result of clamping <paramref name="value" /> to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</returns>
        /// <exception cref="ArgumentException"><paramref name="min" /> is greater than <paramref name="max" />.</exception>
        static virtual TSelf Clamp(TSelf value, TSelf min, TSelf max)
        {
            if (min > max)
            {
                Math.ThrowMinMaxException(min, max);
            }

            TSelf result = value;

            result = TSelf.Max(result, min);
            result = TSelf.Min(result, max);

            return result;
        }

        /// <summary>Clamps a value to an inclusive minimum and maximum value using platform-specific behavior for <c>NaN</c> and <c>NegativeZero</c>.</summary>
        /// <param name="value">The value to clamp.</param>
        /// <param name="min">The inclusive minimum to which <paramref name="value" /> should clamp.</param>
        /// <param name="max">The inclusive maximum to which <paramref name="value" /> should clamp.</param>
        /// <returns>The result of clamping <paramref name="value" /> to the inclusive range of <paramref name="min" /> and <paramref name="max" />.</returns>
        /// <exception cref="ArgumentException"><paramref name="min" /> is greater than <paramref name="max" />.</exception>
        static virtual TSelf ClampNative(TSelf value, TSelf min, TSelf max)
        {
            if (min > max)
            {
                Math.ThrowMinMaxException(min, max);
            }

            TSelf result = value;

            result = TSelf.MaxNative(result, min);
            result = TSelf.MinNative(result, max);

            return result;
        }

        /// <summary>Copies the sign of a value to the sign of another value.</summary>
        /// <param name="value">The value whose magnitude is used in the result.</param>
        /// <param name="sign">The value whose sign is used in the result.</param>
        /// <returns>A value with the magnitude of <paramref name="value" /> and the sign of <paramref name="sign" />.</returns>
        static virtual TSelf CopySign(TSelf value, TSelf sign)
        {
            TSelf result = value;

            if (TSelf.IsNegative(value) != TSelf.IsNegative(sign))
            {
                result = checked(-result);
            }

            return result;
        }

        /// <summary>Compares two values to compute which is greater.</summary>
        /// <param name="x">The value to compare with <paramref name="y" />.</param>
        /// <param name="y">The value to compare with <paramref name="x" />.</param>
        /// <returns><paramref name="x" /> if it is greater than <paramref name="y" />; otherwise, <paramref name="y" />.</returns>
        /// <remarks>For <see cref="IFloatingPoint{TSelf}" /> this method matches the IEEE 754:2019 <c>maximum</c> function. This requires NaN inputs to be propagated back to the caller and for <c>-0.0</c> to be treated as less than <c>+0.0</c>.</remarks>
        static virtual TSelf Max(TSelf x, TSelf y)
        {
            // This matches the IEEE 754:2019 `maximum` function
            //
            // It propagates NaN inputs back to the caller and
            // otherwise returns the larger of the inputs. It
            // treats +0 as larger than -0 as per the specification.

            if (x != y)
            {
                if (!TSelf.IsNaN(x))
                {
                    return y < x ? x : y;
                }

                return x;
            }

            return TSelf.IsNegative(y) ? x : y;
        }

        /// <summary>Compares two values to compute which is greater using platform-specific behavior for <c>NaN</c> and <c>NegativeZero</c>.</summary>
        /// <param name="x">The value to compare with <paramref name="y" />.</param>
        /// <param name="y">The value to compare with <paramref name="x" />.</param>
        /// <returns><paramref name="x" /> if it is greater than <paramref name="y" />; otherwise, <paramref name="y" />.</returns>
        static virtual TSelf MaxNative(TSelf x, TSelf y)
        {
            return (x > y) ? x : y;
        }

        /// <summary>Compares two values to compute which is greater and returning the other value if an input is <c>NaN</c>.</summary>
        /// <param name="x">The value to compare with <paramref name="y" />.</param>
        /// <param name="y">The value to compare with <paramref name="x" />.</param>
        /// <returns><paramref name="x" /> if it is greater than <paramref name="y" />; otherwise, <paramref name="y" />.</returns>
        /// <remarks>For <see cref="IFloatingPoint{TSelf}" /> this method matches the IEEE 754:2019 <c>maximumNumber</c> function. This requires NaN inputs to not be propagated back to the caller and for <c>-0.0</c> to be treated as less than <c>+0.0</c>.</remarks>
        static virtual TSelf MaxNumber(TSelf x, TSelf y)
        {
            // This matches the IEEE 754:2019 `maximumNumber` function
            //
            // It does not propagate NaN inputs back to the caller and
            // otherwise returns the larger of the inputs. It
            // treats +0 as larger than -0 as per the specification.

            if (x != y)
            {
                if (!TSelf.IsNaN(y))
                {
                    return y < x ? x : y;
                }

                return x;
            }

            return TSelf.IsNegative(y) ? x : y;
        }

        /// <summary>Compares two values to compute which is lesser.</summary>
        /// <param name="x">The value to compare with <paramref name="y" />.</param>
        /// <param name="y">The value to compare with <paramref name="x" />.</param>
        /// <returns><paramref name="x" /> if it is less than <paramref name="y" />; otherwise, <paramref name="y" />.</returns>
        /// <remarks>For <see cref="IFloatingPoint{TSelf}" /> this method matches the IEEE 754:2019 <c>minimum</c> function. This requires NaN inputs to be propagated back to the caller and for <c>-0.0</c> to be treated as less than <c>+0.0</c>.</remarks>
        static virtual TSelf Min(TSelf x, TSelf y)
        {
            // This matches the IEEE 754:2019 `minimum` function
            //
            // It propagates NaN inputs back to the caller and
            // otherwise returns the larger of the inputs. It
            // treats +0 as larger than -0 as per the specification.

            if (x != y)
            {
                if (!TSelf.IsNaN(x))
                {
                    return x < y ? x : y;
                }

                return x;
            }

            return TSelf.IsNegative(x) ? x : y;
        }

        /// <summary>Compares two values to compute which is lesser using platform-specific behavior for <c>NaN</c> and <c>NegativeZero</c>.</summary>
        /// <param name="x">The value to compare with <paramref name="y" />.</param>
        /// <param name="y">The value to compare with <paramref name="x" />.</param>
        /// <returns><paramref name="x" /> if it is lesser than <paramref name="y" />; otherwise, <paramref name="y" />.</returns>
        static virtual TSelf MinNative(TSelf x, TSelf y)
        {
            return (x < y) ? x : y;
        }

        /// <summary>Compares two values to compute which is lesser and returning the other value if an input is <c>NaN</c>.</summary>
        /// <param name="x">The value to compare with <paramref name="y" />.</param>
        /// <param name="y">The value to compare with <paramref name="x" />.</param>
        /// <returns><paramref name="x" /> if it is less than <paramref name="y" />; otherwise, <paramref name="y" />.</returns>
        /// <remarks>For <see cref="IFloatingPoint{TSelf}" /> this method matches the IEEE 754:2019 <c>minimumNumber</c> function. This requires NaN inputs to not be propagated back to the caller and for <c>-0.0</c> to be treated as less than <c>+0.0</c>.</remarks>
        static virtual TSelf MinNumber(TSelf x, TSelf y)
        {
            // This matches the IEEE 754:2019 `minimumNumber` function
            //
            // It does not propagate NaN inputs back to the caller and
            // otherwise returns the larger of the inputs. It
            // treats +0 as larger than -0 as per the specification.

            if (x != y)
            {
                if (!TSelf.IsNaN(y))
                {
                    return x < y ? x : y;
                }

                return x;
            }

            return TSelf.IsNegative(x) ? x : y;
        }

        /// <summary>Computes the sign of a value.</summary>
        /// <param name="value">The value whose sign is to be computed.</param>
        /// <returns>A positive value if <paramref name="value" /> is positive, <c>0</c> if <paramref name="value" /> is zero, and a negative value if <paramref name="value" /> is negative.</returns>
        /// <remarks>It is recommended that a function return <c>1</c>, <c>0</c>, and <c>-1</c>, respectively.</remarks>
        static virtual int Sign(TSelf value)
        {
            if (value != TSelf.Zero)
            {
                if (TSelf.IsNaN(value))
                {
                    ThrowHelper.ThrowArithmeticException(SR.Arithmetic_NaN);
                }
                return TSelf.IsNegative(value) ? -1 : +1;
            }
            return 0;
        }
    }
    /// <summary>Defines the base of other number types.</summary>
    /// <typeparam name="TSelf">The type that implements the interface.</typeparam>
    public interface INumberBase<TSelf>
        : IAdditionOperators<TSelf, TSelf, TSelf>,
          IAdditiveIdentity<TSelf, TSelf>,
          IDecrementOperators<TSelf>,
          IDivisionOperators<TSelf, TSelf, TSelf>,
          IEquatable<TSelf>,
          IEqualityOperators<TSelf, TSelf, bool>,
          IIncrementOperators<TSelf>,
          IMultiplicativeIdentity<TSelf, TSelf>,
          IMultiplyOperators<TSelf, TSelf, TSelf>,
          ISpanFormattable,
          ISpanParsable<TSelf>,
          ISubtractionOperators<TSelf, TSelf, TSelf>,
          IUnaryPlusOperators<TSelf, TSelf>,
          IUnaryNegationOperators<TSelf, TSelf>,
          IUtf8SpanFormattable,
          IUtf8SpanParsable<TSelf>
        where TSelf : INumberBase<TSelf>?
    {
        /// <summary>Gets the value <c>1</c> for the type.</summary>
        static abstract TSelf One { get; }

        /// <summary>Gets the radix, or base, for the type.</summary>
        static abstract int Radix { get; }

        /// <summary>Gets the value <c>0</c> for the type.</summary>
        static abstract TSelf Zero { get; }

        /// <summary>Computes the absolute of a value.</summary>
        /// <param name="value">The value for which to get its absolute.</param>
        /// <returns>The absolute of <paramref name="value" />.</returns>
        /// <exception cref="OverflowException">The absolute of <paramref name="value" /> is not representable by <typeparamref name="TSelf" />.</exception>
        static abstract TSelf Abs(TSelf value);

        /// <summary>Creates an instance of the current type from a value, throwing an overflow exception for any values that fall outside the representable range of the current type.</summary>
        /// <typeparam name="TOther">The type of <paramref name="value" />.</typeparam>
        /// <param name="value">The value which is used to create the instance of <typeparamref name="TSelf" />.</param>
        /// <returns>An instance of <typeparamref name="TSelf" /> created from <paramref name="value" />.</returns>
        /// <exception cref="NotSupportedException"><typeparamref name="TOther" /> is not supported.</exception>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <typeparamref name="TSelf" />.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static virtual TSelf CreateChecked<TOther>(TOther value)
#nullable disable
            where TOther : INumberBase<TOther>
#nullable restore
        {
            TSelf? result;

            if (typeof(TOther) == typeof(TSelf))
            {
                result = (TSelf)(object)value;
            }
            else if (!TSelf.TryConvertFromChecked(value, out result) && !TOther.TryConvertToChecked<TSelf>(value, out result))
            {
                ThrowHelper.ThrowNotSupportedException();
            }

            return result;
        }

        /// <summary>Creates an instance of the current type from a value, saturating any values that fall outside the representable range of the current type.</summary>
        /// <typeparam name="TOther">The type of <paramref name="value" />.</typeparam>
        /// <param name="value">The value which is used to create the instance of <typeparamref name="TSelf" />.</param>
        /// <returns>An instance of <typeparamref name="TSelf" /> created from <paramref name="value" />, saturating if <paramref name="value" /> falls outside the representable range of <typeparamref name="TSelf" />.</returns>
        /// <exception cref="NotSupportedException"><typeparamref name="TOther" /> is not supported.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static virtual TSelf CreateSaturating<TOther>(TOther value)
#nullable disable
            where TOther : INumberBase<TOther>
#nullable restore
        {
            TSelf? result;

            if (typeof(TOther) == typeof(TSelf))
            {
                result = (TSelf)(object)value;
            }
            else if (!TSelf.TryConvertFromSaturating(value, out result) && !TOther.TryConvertToSaturating<TSelf>(value, out result))
            {
                ThrowHelper.ThrowNotSupportedException();
            }

            return result;
        }

        /// <summary>Creates an instance of the current type from a value, truncating any values that fall outside the representable range of the current type.</summary>
        /// <typeparam name="TOther">The type of <paramref name="value" />.</typeparam>
        /// <param name="value">The value which is used to create the instance of <typeparamref name="TSelf" />.</param>
        /// <returns>An instance of <typeparamref name="TSelf" /> created from <paramref name="value" />, truncating if <paramref name="value" /> falls outside the representable range of <typeparamref name="TSelf" />.</returns>
        /// <exception cref="NotSupportedException"><typeparamref name="TOther" /> is not supported.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static virtual TSelf CreateTruncating<TOther>(TOther value)
#nullable disable
            where TOther : INumberBase<TOther>
#nullable restore
        {
            TSelf? result;

            if (typeof(TOther) == typeof(TSelf))
            {
                result = (TSelf)(object)value;
            }
            else if (!TSelf.TryConvertFromTruncating(value, out result) && !TOther.TryConvertToTruncating<TSelf>(value, out result))
            {
                ThrowHelper.ThrowNotSupportedException();
            }

            return result;
        }

        /// <summary>Determines if a value is in its canonical representation.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is in its canonical representation; otherwise, <c>false</c>.</returns>
        static abstract bool IsCanonical(TSelf value);

        /// <summary>Determines if a value represents a complex value.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is a complex number; otherwise, <c>false</c>.</returns>
        /// <remarks>This function returns <c>false</c> for a complex number <c>a + bi</c> where either <c>a</c> or <c>b</c> is zero. In other words, it excludes real numbers and pure imaginary numbers.</remarks>
        static abstract bool IsComplexNumber(TSelf value);

        /// <summary>Determines if a value represents an even integral value.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is an even integer; otherwise, <c>false</c>.</returns>
        /// <remarks>
        ///     <para>This correctly handles floating-point values and so <c>2.0</c> will return <c>true</c> while <c>2.2</c> will return <c>false</c>.</para>
        ///     <para>This functioning returning <c>false</c> does not imply that <see cref="IsOddInteger(TSelf)" /> will return <c>true</c>. A number with a fractional portion, <c>3.3</c>, is not even nor odd.</para>
        /// </remarks>
        static abstract bool IsEvenInteger(TSelf value);

        /// <summary>Determines if a value is finite.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is finite; otherwise, <c>false</c>.</returns>
        /// <remarks>This function returning <c>false</c> does not imply that <see cref="IsInfinity(TSelf)" /> will return <c>true</c>. <c>NaN</c> is not finite nor infinite.</remarks>
        static abstract bool IsFinite(TSelf value);

        /// <summary>Determines if a value represents a pure imaginary value.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is a pure imaginary number; otherwise, <c>false</c>.</returns>
        /// <remarks>This function returns <c>false</c> for a complex number <c>a + bi</c> where <c>a</c> is non-zero, as that number is not purely imaginary.</remarks>
        static abstract bool IsImaginaryNumber(TSelf value);

        /// <summary>Determines if a value is infinite.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is infinite; otherwise, <c>false</c>.</returns>
        /// <remarks>This function returning <c>false</c> does not imply that <see cref="IsFinite(TSelf)" /> will return <c>true</c>. <c>NaN</c> is not finite nor infinite.</remarks>
        static abstract bool IsInfinity(TSelf value);

        /// <summary>Determines if a value represents an integral value.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is an integer; otherwise, <c>false</c>.</returns>
        /// <remarks>This correctly handles floating-point values and so <c>2.0</c> and <c>3.0</c> will return <c>true</c> while <c>2.2</c> and <c>3.3</c> will return <c>false</c>.</remarks>
        static abstract bool IsInteger(TSelf value);

        /// <summary>Determines if a value is NaN.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is NaN; otherwise, <c>false</c>.</returns>
        static abstract bool IsNaN(TSelf value);

        /// <summary>Determines if a value represents a negative real number.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> represents negative zero or a negative real number; otherwise, <c>false</c>.</returns>
        /// <remarks>
        ///     <para>If this type has signed zero, then <c>-0</c> is also considered negative.</para>
        ///     <para>This function returning <c>false</c> does not imply that <see cref="IsPositive(TSelf)" /> will return <c>true</c>. A complex number, <c>a + bi</c> for non-zero <c>b</c>, is not positive nor negative</para>
        /// </remarks>
        static abstract bool IsNegative(TSelf value);

        /// <summary>Determines if a value is negative infinity.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is negative infinity; otherwise, <c>false</c>.</returns>
        static abstract bool IsNegativeInfinity(TSelf value);

        /// <summary>Determines if a value is normal.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is normal; otherwise, <c>false</c>.</returns>
        static abstract bool IsNormal(TSelf value);

        /// <summary>Determines if a value represents an odd integral value.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is an odd integer; otherwise, <c>false</c>.</returns>
        /// <remarks>
        ///     <para>This correctly handles floating-point values and so <c>3.0</c> will return <c>true</c> while <c>3.3</c> will return <c>false</c>.</para>
        ///     <para>This functioning returning <c>false</c> does not imply that <see cref="IsEvenInteger(TSelf)" /> will return <c>true</c>. A number with a fractional portion, <c>3.3</c>, is neither even nor odd.</para>
        /// </remarks>
        static abstract bool IsOddInteger(TSelf value);

        /// <summary>Determines if a value represents zero or a positive real number.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> represents (positive) zero or a positive real number; otherwise, <c>false</c>.</returns>
        /// <remarks>
        ///     <para>If this type has signed zero, then <c>-0</c> is not considered positive, but <c>+0</c> is.</para>
        ///     <para>This function returning <c>false</c> does not imply that <see cref="IsNegative(TSelf)" /> will return <c>true</c>. A complex number, <c>a + bi</c> for non-zero <c>b</c>, is not positive nor negative</para>
        /// </remarks>
        static abstract bool IsPositive(TSelf value);

        /// <summary>Determines if a value is positive infinity.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is positive infinity; otherwise, <c>false</c>.</returns>
        static abstract bool IsPositiveInfinity(TSelf value);

        /// <summary>Determines if a value represents a real value.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is a real number; otherwise, <c>false</c>.</returns>
        /// <remarks>
        ///     <para>This function returns <c>true</c> for a complex number <c>a + bi</c> where <c>b</c> is zero.</para>
        ///     <para>This function checks values against the extended real number line, thus returns <c>true</c> for positive and negative infinity.</para>
        /// </remarks>
        static abstract bool IsRealNumber(TSelf value);

        /// <summary>Determines if a value is subnormal.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is subnormal; otherwise, <c>false</c>.</returns>
        static abstract bool IsSubnormal(TSelf value);

        /// <summary>Determines if a value is zero.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is zero; otherwise, <c>false</c>.</returns>
        /// <remarks>This function treats both positive and negative zero as zero and so will return <c>true</c> for <c>+0.0</c> and <c>-0.0</c>.</remarks>
        static abstract bool IsZero(TSelf value);

        /// <summary>Compares two values to compute which has the greater magnitude.</summary>
        /// <param name="x">The value to compare with <paramref name="y" />.</param>
        /// <param name="y">The value to compare with <paramref name="x" />.</param>
        /// <returns><paramref name="x" /> if it has a greater magnitude than <paramref name="y" />; otherwise, <paramref name="y" />.</returns>
        /// <remarks>For <see cref="IFloatingPointIeee754{TSelf}" /> this method matches the IEEE 754:2019 <c>maximumMagnitude</c> function. This requires NaN inputs to be propagated back to the caller and for <c>-0.0</c> to be treated as less than <c>+0.0</c>.</remarks>
        static abstract TSelf MaxMagnitude(TSelf x, TSelf y);

        /// <summary>Compares two values to compute which has the greater magnitude and returning the other value if an input is <c>NaN</c>.</summary>
        /// <param name="x">The value to compare with <paramref name="y" />.</param>
        /// <param name="y">The value to compare with <paramref name="x" />.</param>
        /// <returns><paramref name="x" /> if it has a greater magnitude than <paramref name="y" />; otherwise, <paramref name="y" />.</returns>
        /// <remarks>For <see cref="IFloatingPointIeee754{TSelf}" /> this method matches the IEEE 754:2019 <c>maximumMagnitudeNumber</c> function. This requires NaN inputs to not be propagated back to the caller and for <c>-0.0</c> to be treated as less than <c>+0.0</c>.</remarks>
        static abstract TSelf MaxMagnitudeNumber(TSelf x, TSelf y);

        /// <summary>Compares two values to compute which has the lesser magnitude.</summary>
        /// <param name="x">The value to compare with <paramref name="y" />.</param>
        /// <param name="y">The value to compare with <paramref name="x" />.</param>
        /// <returns><paramref name="x" /> if it has a lesser magnitude than <paramref name="y" />; otherwise, <paramref name="y" />.</returns>
        /// <remarks>For <see cref="IFloatingPointIeee754{TSelf}" /> this method matches the IEEE 754:2019 <c>minimumMagnitude</c> function. This requires NaN inputs to be propagated back to the caller and for <c>-0.0</c> to be treated as less than <c>+0.0</c>.</remarks>
        static abstract TSelf MinMagnitude(TSelf x, TSelf y);

        /// <summary>Compares two values to compute which has the lesser magnitude and returning the other value if an input is <c>NaN</c>.</summary>
        /// <param name="x">The value to compare with <paramref name="y" />.</param>
        /// <param name="y">The value to compare with <paramref name="x" />.</param>
        /// <returns><paramref name="x" /> if it has a lesser magnitude than <paramref name="y" />; otherwise, <paramref name="y" />.</returns>
        /// <remarks>For <see cref="IFloatingPointIeee754{TSelf}" /> this method matches the IEEE 754:2019 <c>minimumMagnitudeNumber</c> function. This requires NaN inputs to not be propagated back to the caller and for <c>-0.0</c> to be treated as less than <c>+0.0</c>.</remarks>
        static abstract TSelf MinMagnitudeNumber(TSelf x, TSelf y);

        /// <summary>Computes an estimate of (<paramref name="left"/> * <paramref name="right"/>) + <paramref name="addend"/>.</summary>
        /// <param name="left">The value to be multiplied with <paramref name="right" />.</param>
        /// <param name="right">The value to be multiplied with <paramref name="left" />.</param>
        /// <param name="addend">The value to be added to the result of <paramref name="left" /> multiplied by <paramref name="right" />.</param>
        /// <returns>An estimate of (<paramref name="left"/> * <paramref name="right"/>) + <paramref name="addend"/>.</returns>
        /// <remarks>
        ///   <para>On hardware that natively supports <see cref="IFloatingPointIeee754{TSelf}.FusedMultiplyAdd" />, this may return a result that was rounded as one ternary operation.</para>
        ///   <para>On hardware without specialized support, this may just return (<paramref name="left"/> * <paramref name="right"/>) + <paramref name="addend"/>.</para>
        /// </remarks>
        static virtual TSelf MultiplyAddEstimate(TSelf left, TSelf right, TSelf addend) => (left * right) + addend;

        /// <summary>Parses a string into a value.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="style">A bitwise combination of number styles that can be present in <paramref name="s" />.</param>
        /// <param name="provider">An object that provides culture-specific formatting information about <paramref name="s" />.</param>
        /// <returns>The result of parsing <paramref name="s" />.</returns>
        /// <exception cref="ArgumentException"><paramref name="style" /> is not a supported <see cref="NumberStyles" /> value.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="s" /> is <c>null</c>.</exception>
        /// <exception cref="FormatException"><paramref name="s" /> is not in the correct format.</exception>
        /// <exception cref="OverflowException"><paramref name="s" /> is not representable by <typeparamref name="TSelf" />.</exception>
        static abstract TSelf Parse(string s, NumberStyles style, IFormatProvider? provider);

        /// <summary>Parses a span of characters into a value.</summary>
        /// <param name="s">The span of characters to parse.</param>
        /// <param name="style">A bitwise combination of number styles that can be present in <paramref name="s" />.</param>
        /// <param name="provider">An object that provides culture-specific formatting information about <paramref name="s" />.</param>
        /// <returns>The result of parsing <paramref name="s" />.</returns>
        /// <exception cref="ArgumentException"><paramref name="style" /> is not a supported <see cref="NumberStyles" /> value.</exception>
        /// <exception cref="FormatException"><paramref name="s" /> is not in the correct format.</exception>
        /// <exception cref="OverflowException"><paramref name="s" /> is not representable by <typeparamref name="TSelf" />.</exception>
        static abstract TSelf Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider);

        /// <summary>Parses a span of UTF-8 characters into a value.</summary>
        /// <param name="utf8Text">The span of UTF-8 characters to parse.</param>
        /// <param name="style">A bitwise combination of number styles that can be present in <paramref name="utf8Text" />.</param>
        /// <param name="provider">An object that provides culture-specific formatting information about <paramref name="utf8Text" />.</param>
        /// <returns>The result of parsing <paramref name="utf8Text" />.</returns>
        /// <exception cref="ArgumentException"><paramref name="style" /> is not a supported <see cref="NumberStyles" /> value.</exception>
        /// <exception cref="FormatException"><paramref name="utf8Text" /> is not in the correct format.</exception>
        /// <exception cref="OverflowException"><paramref name="utf8Text" /> is not representable by <typeparamref name="TSelf" />.</exception>
        static virtual TSelf Parse(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider? provider)
        {
            // Convert text using stackalloc for <= 256 characters and ArrayPool otherwise

            char[]? utf16TextArray;
            scoped Span<char> utf16Text;
            int textMaxCharCount = Encoding.UTF8.GetMaxCharCount(utf8Text.Length);

            if (textMaxCharCount <= 256)
            {
                utf16TextArray = null;
                utf16Text = stackalloc char[256];
            }
            else
            {
                utf16TextArray = ArrayPool<char>.Shared.Rent(textMaxCharCount);
                utf16Text = utf16TextArray;
            }

            OperationStatus utf8TextStatus = Utf8.ToUtf16(utf8Text, utf16Text, out _, out int utf16TextLength, replaceInvalidSequences: false);

            if (utf8TextStatus != OperationStatus.Done)
            {
                if (utf16TextArray != null)
                {
                    // Return rented buffers if necessary
                    ArrayPool<char>.Shared.Return(utf16TextArray);
                }

                ThrowHelper.ThrowFormatInvalidString();
            }
            utf16Text = utf16Text.Slice(0, utf16TextLength);

            // Actual operation

            TSelf result = TSelf.Parse(utf16Text, style, provider);

            // Return rented buffers if necessary

            if (utf16TextArray != null)
            {
                ArrayPool<char>.Shared.Return(utf16TextArray);
            }

            return result;
        }

        /// <summary>Tries to convert a value to an instance of the current type, throwing an overflow exception for any values that fall outside the representable range of the current type.</summary>
        /// <typeparam name="TOther">The type of <paramref name="value" />.</typeparam>
        /// <param name="value">The value which is used to create the instance of <typeparamref name="TSelf" />.</param>
        /// <param name="result">On return, contains an instance of <typeparamref name="TSelf" /> converted from <paramref name="value" />.</param>
        /// <returns><c>false</c> if <typeparamref name="TOther" /> is not supported; otherwise, <c>true</c>.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <typeparamref name="TSelf" />.</exception>
        protected static abstract bool TryConvertFromChecked<TOther>(TOther value, [MaybeNullWhen(false)] out TSelf result)
#nullable disable
            where TOther : INumberBase<TOther>;
#nullable restore

        /// <summary>Tries to convert a value to an instance of the current type, saturating any values that fall outside the representable range of the current type.</summary>
        /// <typeparam name="TOther">The type of <paramref name="value" />.</typeparam>
        /// <param name="value">The value which is used to create the instance of <typeparamref name="TSelf" />.</param>
        /// <param name="result">On return, contains an instance of <typeparamref name="TSelf" /> converted from <paramref name="value" />.</param>
        /// <returns><c>false</c> if <typeparamref name="TOther" /> is not supported; otherwise, <c>true</c>.</returns>
        protected static abstract bool TryConvertFromSaturating<TOther>(TOther value, [MaybeNullWhen(false)] out TSelf result)
#nullable disable
            where TOther : INumberBase<TOther>;
#nullable restore

        /// <summary>Tries to convert a value to an instance of the current type, truncating any values that fall outside the representable range of the current type.</summary>
        /// <typeparam name="TOther">The type of <paramref name="value" />.</typeparam>
        /// <param name="value">The value which is used to create the instance of <typeparamref name="TSelf" />.</param>
        /// <param name="result">On return, contains an instance of <typeparamref name="TSelf" /> converted from <paramref name="value" />.</param>
        /// <returns><c>false</c> if <typeparamref name="TOther" /> is not supported; otherwise, <c>true</c>.</returns>
        protected static abstract bool TryConvertFromTruncating<TOther>(TOther value, [MaybeNullWhen(false)] out TSelf result)
#nullable disable
            where TOther : INumberBase<TOther>;
#nullable restore

        /// <summary>Tries to convert an instance of the current type to another type, throwing an overflow exception for any values that fall outside the representable range of the current type.</summary>
        /// <typeparam name="TOther">The type to which <paramref name="value" /> should be converted.</typeparam>
        /// <param name="value">The value which is used to create the instance of <typeparamref name="TOther" />.</param>
        /// <param name="result">On return, contains an instance of <typeparamref name="TOther" /> converted from <paramref name="value" />.</param>
        /// <returns><c>false</c> if <typeparamref name="TOther" /> is not supported; otherwise, <c>true</c>.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <typeparamref name="TOther" />.</exception>
        protected static abstract bool TryConvertToChecked<TOther>(TSelf value, [MaybeNullWhen(false)] out TOther result)
#nullable disable
            where TOther : INumberBase<TOther>;
#nullable restore

        /// <summary>Tries to convert an instance of the current type to another type, saturating any values that fall outside the representable range of the current type.</summary>
        /// <typeparam name="TOther">The type to which <paramref name="value" /> should be converted.</typeparam>
        /// <param name="value">The value which is used to create the instance of <typeparamref name="TOther" />.</param>
        /// <param name="result">On return, contains an instance of <typeparamref name="TOther" /> converted from <paramref name="value" />.</param>
        /// <returns><c>false</c> if <typeparamref name="TOther" /> is not supported; otherwise, <c>true</c>.</returns>
        protected static abstract bool TryConvertToSaturating<TOther>(TSelf value, [MaybeNullWhen(false)] out TOther result)
#nullable disable
            where TOther : INumberBase<TOther>;
#nullable restore

        /// <summary>Tries to convert an instance of the current type to another type, truncating any values that fall outside the representable range of the current type.</summary>
        /// <typeparam name="TOther">The type to which <paramref name="value" /> should be converted.</typeparam>
        /// <param name="value">The value which is used to create the instance of <typeparamref name="TOther" />.</param>
        /// <param name="result">On return, contains an instance of <typeparamref name="TOther" /> converted from <paramref name="value" />.</param>
        /// <returns><c>false</c> if <typeparamref name="TOther" /> is not supported; otherwise, <c>true</c>.</returns>
        protected static abstract bool TryConvertToTruncating<TOther>(TSelf value, [MaybeNullWhen(false)] out TOther result)
#nullable disable
            where TOther : INumberBase<TOther>;
#nullable restore

        /// <summary>Tries to parse a string into a value.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="style">A bitwise combination of number styles that can be present in <paramref name="s" />.</param>
        /// <param name="provider">An object that provides culture-specific formatting information about <paramref name="s" />.</param>
        /// <param name="result">On return, contains the result of successfully parsing <paramref name="s" /> or an undefined value on failure.</param>
        /// <returns><c>true</c> if <paramref name="s" /> was successfully parsed; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentException"><paramref name="style" /> is not a supported <see cref="NumberStyles" /> value.</exception>
        static abstract bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result);

        /// <summary>Tries to parse a span of characters into a value.</summary>
        /// <param name="s">The span of characters to parse.</param>
        /// <param name="style">A bitwise combination of number styles that can be present in <paramref name="s" />.</param>
        /// <param name="provider">An object that provides culture-specific formatting information about <paramref name="s" />.</param>
        /// <param name="result">On return, contains the result of successfully parsing <paramref name="s" /> or an undefined value on failure.</param>
        /// <returns><c>true</c> if <paramref name="s" /> was successfully parsed; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentException"><paramref name="style" /> is not a supported <see cref="NumberStyles" /> value.</exception>
        static abstract bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result);

        /// <summary>Tries to parse a span of UTF-8 characters into a value.</summary>
        /// <param name="utf8Text">The span of UTF-8 characters to parse.</param>
        /// <param name="style">A bitwise combination of number styles that can be present in <paramref name="utf8Text" />.</param>
        /// <param name="provider">An object that provides culture-specific formatting information about <paramref name="utf8Text" />.</param>
        /// <param name="result">On return, contains the result of successfully parsing <paramref name="utf8Text" /> or an undefined value on failure.</param>
        /// <returns><c>true</c> if <paramref name="utf8Text" /> was successfully parsed; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentException"><paramref name="style" /> is not a supported <see cref="NumberStyles" /> value.</exception>
        static virtual bool TryParse(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
        {
            // Convert text using stackalloc for <= 256 characters and ArrayPool otherwise

            char[]? utf16TextArray;
            scoped Span<char> utf16Text;
            int textMaxCharCount = Encoding.UTF8.GetMaxCharCount(utf8Text.Length);

            if (textMaxCharCount <= 256)
            {
                utf16TextArray = null;
                utf16Text = stackalloc char[256];
            }
            else
            {
                utf16TextArray = ArrayPool<char>.Shared.Rent(textMaxCharCount);
                utf16Text = utf16TextArray;
            }

            OperationStatus utf8TextStatus = Utf8.ToUtf16(utf8Text, utf16Text, out _, out int utf16TextLength, replaceInvalidSequences: false);

            if (utf8TextStatus != OperationStatus.Done)
            {
                if (utf16TextArray != null)
                {
                    // Return rented buffers if necessary
                    ArrayPool<char>.Shared.Return(utf16TextArray);
                }

                result = default;
                return false;
            }
            utf16Text = utf16Text.Slice(0, utf16TextLength);

            // Actual operation

            bool succeeded = TSelf.TryParse(utf16Text, style, provider, out result);

            // Return rented buffers if necessary

            if (utf16TextArray != null)
            {
                ArrayPool<char>.Shared.Return(utf16TextArray);
            }

            return succeeded;
        }

        bool IUtf8SpanFormattable.TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            char[]? utf16DestinationArray;
            scoped Span<char> utf16Destination;
            int destinationMaxCharCount = Encoding.UTF8.GetMaxCharCount(utf8Destination.Length);

            if (destinationMaxCharCount <= 256)
            {
                utf16DestinationArray = null;
                utf16Destination = stackalloc char[256];
            }
            else
            {
                utf16DestinationArray = ArrayPool<char>.Shared.Rent(destinationMaxCharCount);
                utf16Destination = utf16DestinationArray;
            }

            if (!TryFormat(utf16Destination, out int charsWritten, format, provider))
            {
                if (utf16DestinationArray != null)
                {
                    // Return rented buffers if necessary
                    ArrayPool<char>.Shared.Return(utf16DestinationArray);
                }

                bytesWritten = 0;
                return false;
            }

            // Make sure we slice the buffer to just the characters written
            utf16Destination = utf16Destination.Slice(0, charsWritten);

            OperationStatus utf8DestinationStatus = Utf8.FromUtf16(utf16Destination, utf8Destination, out _, out bytesWritten, replaceInvalidSequences: false);

            if (utf16DestinationArray != null)
            {
                // Return rented buffers if necessary
                ArrayPool<char>.Shared.Return(utf16DestinationArray);
            }

            if (utf8DestinationStatus == OperationStatus.Done)
            {
                return true;
            }

            if (utf8DestinationStatus != OperationStatus.DestinationTooSmall)
            {
                ThrowHelper.ThrowInvalidOperationException_InvalidUtf8();
            }

            bytesWritten = 0;
            return false;
        }

        static TSelf IUtf8SpanParsable<TSelf>.Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
        {
            // Convert text using stackalloc for <= 256 characters and ArrayPool otherwise

            char[]? utf16TextArray;
            scoped Span<char> utf16Text;
            int textMaxCharCount = Encoding.UTF8.GetMaxCharCount(utf8Text.Length);

            if (textMaxCharCount <= 256)
            {
                utf16TextArray = null;
                utf16Text = stackalloc char[256];
            }
            else
            {
                utf16TextArray = ArrayPool<char>.Shared.Rent(textMaxCharCount);
                utf16Text = utf16TextArray;
            }

            OperationStatus utf8TextStatus = Utf8.ToUtf16(utf8Text, utf16Text, out _, out int utf16TextLength, replaceInvalidSequences: false);

            if (utf8TextStatus != OperationStatus.Done)
            {
                if (utf16TextArray != null)
                {
                    // Return rented buffers if necessary
                    ArrayPool<char>.Shared.Return(utf16TextArray);
                }

                ThrowHelper.ThrowFormatInvalidString();
            }
            utf16Text = utf16Text.Slice(0, utf16TextLength);

            // Actual operation

            TSelf result = TSelf.Parse(utf16Text, provider);

            // Return rented buffers if necessary

            if (utf16TextArray != null)
            {
                ArrayPool<char>.Shared.Return(utf16TextArray);
            }

            return result;
        }

        static bool IUtf8SpanParsable<TSelf>.TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [MaybeNullWhen(returnValue: false)] out TSelf result)
        {
            // Convert text using stackalloc for <= 256 characters and ArrayPool otherwise

            char[]? utf16TextArray;
            scoped Span<char> utf16Text;
            int textMaxCharCount = Encoding.UTF8.GetMaxCharCount(utf8Text.Length);

            if (textMaxCharCount <= 256)
            {
                utf16TextArray = null;
                utf16Text = stackalloc char[256];
            }
            else
            {
                utf16TextArray = ArrayPool<char>.Shared.Rent(textMaxCharCount);
                utf16Text = utf16TextArray;
            }

            OperationStatus utf8TextStatus = Utf8.ToUtf16(utf8Text, utf16Text, out _, out int utf16TextLength, replaceInvalidSequences: false);

            if (utf8TextStatus != OperationStatus.Done)
            {
                if (utf16TextArray != null)
                {
                    // Return rented buffers if necessary
                    ArrayPool<char>.Shared.Return(utf16TextArray);
                }

                result = default;
                return false;
            }
            utf16Text = utf16Text.Slice(0, utf16TextLength);

            // Actual operation

            bool succeeded = TSelf.TryParse(utf16Text, provider, out result);

            // Return rented buffers if necessary

            if (utf16TextArray != null)
            {
                ArrayPool<char>.Shared.Return(utf16TextArray);
            }

            return succeeded;
        }
    }
    /// <summary>Defines support for power functions.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    public interface IPowerFunctions<TSelf>
        : INumberBase<TSelf>
        where TSelf : IPowerFunctions<TSelf>?
    {
        /// <summary>Computes a value raised to a given power.</summary>
        /// <param name="x">The value which is raised to the power of <paramref name="x" />.</param>
        /// <param name="y">The power to which <paramref name="x" /> is raised.</param>
        /// <returns><paramref name="x" /> raised to the power of <paramref name="y" />.</returns>
        static abstract TSelf Pow(TSelf x, TSelf y);
    }
    /// <summary>Defines support for root functions.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    public interface IRootFunctions<TSelf>
        : IFloatingPointConstants<TSelf>
        where TSelf : IRootFunctions<TSelf>?
    {
        /// <summary>Computes the cube-root of a value.</summary>
        /// <param name="x">The value whose cube-root is to be computed.</param>
        /// <returns>The cube-root of <paramref name="x" />.</returns>
        static abstract TSelf Cbrt(TSelf x);

        /// <summary>Computes the hypotenuse given two values representing the lengths of the shorter sides in a right-angled triangle.</summary>
        /// <param name="x">The value to square and add to <paramref name="y" />.</param>
        /// <param name="y">The value to square and add to <paramref name="x" />.</param>
        /// <returns>The square root of <paramref name="x" />-squared plus <paramref name="y" />-squared.</returns>
        static abstract TSelf Hypot(TSelf x, TSelf y);

        /// <summary>Computes the n-th root of a value.</summary>
        /// <param name="x">The value whose <paramref name="n" />-th root is to be computed.</param>
        /// <param name="n">The degree of the root to be computed.</param>
        /// <returns>The <paramref name="n" />-th root of <paramref name="x" />.</returns>
        static abstract TSelf RootN(TSelf x, int n);

        /// <summary>Computes the square-root of a value.</summary>
        /// <param name="x">The value whose square-root is to be computed.</param>
        /// <returns>The square-root of <paramref name="x" />.</returns>
        static abstract TSelf Sqrt(TSelf x);
    }
    /// <summary>Defines a mechanism for shifting a value by another value.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    /// <typeparam name="TOther">The type used to specify the amount by which <typeparamref name="TSelf" /> should be shifted.</typeparam>
    /// <typeparam name="TResult">The type that contains the result of shifting <typeparamref name="TSelf" /> by <typeparamref name="TResult" />.</typeparam>
    public interface IShiftOperators<TSelf, TOther, TResult>
        where TSelf : IShiftOperators<TSelf, TOther, TResult>?
    {
        /// <summary>Shifts a value left by a given amount.</summary>
        /// <param name="value">The value which is shifted left by <paramref name="shiftAmount" />.</param>
        /// <param name="shiftAmount">The amount by which <paramref name="value" /> is shifted left.</param>
        /// <returns>The result of shifting <paramref name="value" /> left by <paramref name="shiftAmount" />.</returns>
        static abstract TResult operator <<(TSelf value, TOther shiftAmount);

        /// <summary>Shifts a value right by a given amount.</summary>
        /// <param name="value">The value which is shifted right by <paramref name="shiftAmount" />.</param>
        /// <param name="shiftAmount">The amount by which <paramref name="value" /> is shifted right.</param>
        /// <returns>The result of shifting <paramref name="value" /> right by <paramref name="shiftAmount" />.</returns>
        /// <remarks>This operation is meant to perform a signed (otherwise known as an arithmetic) right shift on signed types.</remarks>
        static abstract TResult operator >>(TSelf value, TOther shiftAmount);

        /// <summary>Shifts a value right by a given amount.</summary>
        /// <param name="value">The value which is shifted right by <paramref name="shiftAmount" />.</param>
        /// <param name="shiftAmount">The amount by which <paramref name="value" /> is shifted right.</param>
        /// <returns>The result of shifting <paramref name="value" /> right by <paramref name="shiftAmount" />.</returns>
        /// <remarks>This operation is meant to perform n unsigned (otherwise known as a logical) right shift on all types.</remarks>
        static abstract TResult operator >>>(TSelf value, TOther shiftAmount);
    }
    /// <summary>Defines a number type which can represent both positive and negative values.</summary>
    /// <typeparam name="TSelf">The type that implements the interface.</typeparam>
    public interface ISignedNumber<TSelf>
        : INumberBase<TSelf>
        where TSelf : ISignedNumber<TSelf>?
    {
        /// <summary>Gets the value <c>-1</c> for the type.</summary>
        static abstract TSelf NegativeOne { get; }
    }
    /// <summary>Defines a mechanism for computing the difference of two values.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    /// <typeparam name="TOther">The type that will be subtracted from <typeparamref name="TSelf" />.</typeparam>
    /// <typeparam name="TResult">The type that contains the difference of <typeparamref name="TOther" /> subtracted from <typeparamref name="TSelf" />.</typeparam>
    public interface ISubtractionOperators<TSelf, TOther, TResult>
        where TSelf : ISubtractionOperators<TSelf, TOther, TResult>?
    {
        /// <summary>Subtracts two values to compute their difference.</summary>
        /// <param name="left">The value from which <paramref name="right" /> is subtracted.</param>
        /// <param name="right">The value which is subtracted from <paramref name="left" />.</param>
        /// <returns>The difference of <paramref name="right" /> subtracted from <paramref name="left" />.</returns>
        static abstract TResult operator -(TSelf left, TOther right);

        /// <summary>Subtracts two values to compute their difference.</summary>
        /// <param name="left">The value from which <paramref name="right" /> is subtracted.</param>
        /// <param name="right">The value which is subtracted from <paramref name="left" />.</param>
        /// <returns>The difference of <paramref name="right" /> subtracted from <paramref name="left" />.</returns>
        /// <exception cref="OverflowException">The difference of <paramref name="right" /> subtracted from <paramref name="left" /> is not representable by <typeparamref name="TResult" />.</exception>
        static virtual TResult operator checked -(TSelf left, TOther right) => left - right;
    }
    /// <summary>Defines support for trigonometric functions.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    public interface ITrigonometricFunctions<TSelf>
        : IFloatingPointConstants<TSelf>
        where TSelf : ITrigonometricFunctions<TSelf>?
    {
        /// <summary>Computes the arc-cosine of a value.</summary>
        /// <param name="x">The value whose arc-cosine is to be computed.</param>
        /// <returns>The arc-cosine of <paramref name="x" />.</returns>
        /// <remarks>This computes <c>arccos(x)</c> in the interval <c>[+0, +PI]</c> radians.</remarks>
        static abstract TSelf Acos(TSelf x);

        /// <summary>Computes the arc-cosine of a value and divides the result by <c>pi</c>.</summary>
        /// <param name="x">The value whose arc-cosine is to be computed.</param>
        /// <returns>The arc-cosine of <paramref name="x" />, divided by <c>pi</c>.</returns>
        /// <remarks>This computes <c>arccos(x) / PI</c> in the interval <c>[-0.5, +0.5]</c>.</remarks>
        static abstract TSelf AcosPi(TSelf x);

        /// <summary>Computes the arc-sine of a value.</summary>
        /// <param name="x">The value whose arc-sine is to be computed.</param>
        /// <returns>The arc-sine of <paramref name="x" />.</returns>
        /// <remarks>This computes <c>arcsin(x)</c> in the interval <c>[-PI / 2, +PI / 2]</c> radians.</remarks>
        static abstract TSelf Asin(TSelf x);

        /// <summary>Computes the arc-sine of a value and divides the result by <c>pi</c>.</summary>
        /// <param name="x">The value whose arc-sine is to be computed.</param>
        /// <returns>The arc-sine of <paramref name="x" />, divided by <c>pi</c>.</returns>
        /// <remarks>This computes <c>arcsin(x) / PI</c> in the interval <c>[-0.5, +0.5]</c>.</remarks>
        static abstract TSelf AsinPi(TSelf x);

        /// <summary>Computes the arc-tangent of a value.</summary>
        /// <param name="x">The value whose arc-tangent is to be computed.</param>
        /// <returns>The arc-tangent of <paramref name="x" />.</returns>
        /// <remarks>This computes <c>arctan(x)</c> in the interval <c>[-PI / 2, +PI / 2]</c> radians.</remarks>
        static abstract TSelf Atan(TSelf x);

        /// <summary>Computes the arc-tangent of a value and divides the result by pi.</summary>
        /// <param name="x">The value whose arc-tangent is to be computed.</param>
        /// <returns>The arc-tangent of <paramref name="x" />, divided by <c>pi</c>.</returns>
        /// <remarks>This computes <c>arctan(x) / PI</c> in the interval <c>[-0.5, +0.5]</c>.</remarks>
        static abstract TSelf AtanPi(TSelf x);

        /// <summary>Computes the cosine of a value.</summary>
        /// <param name="x">The value, in radians, whose cosine is to be computed.</param>
        /// <returns>The cosine of <paramref name="x" />.</returns>
        /// <remarks>This computes <c>cos(x)</c>.</remarks>
        static abstract TSelf Cos(TSelf x);

        /// <summary>Computes the cosine of a value that has been multipled by <c>pi</c>.</summary>
        /// <param name="x">The value, in half-revolutions, whose cosine is to be computed.</param>
        /// <returns>The cosine of <paramref name="x" /> multiplied-by <c>pi</c>.</returns>
        /// <remarks>This computes <c>cos(x * PI)</c>.</remarks>
        static abstract TSelf CosPi(TSelf x);

        /// <summary>Converts a given value from degrees to radians.</summary>
        /// <param name="degrees">The value to convert to radians.</param>
        /// <returns>The value of <paramref name="degrees" /> converted to radians.</returns>
        static virtual TSelf DegreesToRadians(TSelf degrees)
        {
            // We don't want to simplify this to: degrees * (Pi / 180)
            //
            // Doing so will change the result and in many cases will
            // cause a loss of precision. The main exception to this
            // is when the initial multiplication causes overflow, but
            // if we decide that should be handled in the future it needs
            // to be more explicit around how its done.
            //
            // Floating-point operations are naturally imprecise due to
            // rounding required to fit the "infinitely-precise result"
            // into the limits of the underlying representation. Because
            // of this, every operation can introduce some amount of rounding
            // error.
            //
            // For integers, the IEEE 754 binary floating-point types can
            // exactly represent them up to the 2^n, where n is the number
            // of bits in the significand. This is 10 for Half, 23 for Single,
            // and 52 for Double. As you approach this limit, the number of
            // digits available to represent the fractional portion decreases.
            //
            // For Half, you get around 3.311 total decimal digits of precision.
            // For Single, this is around 7.225 and around 15.955 for Double.
            //
            // The actual number of digits can be slightly more or less, depending.
            //
            // This means that values such as `Pi` are not exactly `Pi`, instead:
            // * Half:   3.14 0625
            // * Single: 3.14 1592 741012573 2421875
            // * Double: 3.14 1592 653589793 115997963468544185161590576171875
            // * Actual: 3.14 1592 653589793 2384626433832795028841971693993751058209749445923...
            //
            // If we were to simplify this to simply multiply by (Pi / 180), we get:
            // * Half:   0.01745 6054 6875
            // * Single: 0.01745 3292 384743690 49072265625
            // * Double: 0.01745 3292 519943295 4743716805978692718781530857086181640625
            // * Actual: 0.01745 3292 519943295 7692369076848861271344287188854172545609719144...
            //
            // Neither of these end up "perfect". There will be some cases where they will trade
            // in terms of closeness to the "infinitely precise result". Over the entire domain
            // however, doing the separate multiplications tends to produce overall more accurate
            // results. It helps ensure the implementation can be trivial for the DIM case, and
            // covers the vast majority of typical inputs more efficiently largely only pessimizing
            // the case where the first multiplication results in overflow.
            //
            // This is particularly true for `RadiansToDegrees` where 180 is exactly representable
            // and so allows an exactly representable intermediate value to be computed when overflow
            // doesn't occur.

            return (degrees * TSelf.Pi) / TSelf.CreateChecked(180);
        }

        /// <summary>Converts a given value from radians to degrees.</summary>
        /// <param name="radians">The value to convert to degrees.</param>
        /// <returns>The value of <paramref name="radians" /> converted to degrees.</returns>
        static virtual TSelf RadiansToDegrees(TSelf radians)
        {
            // We don't want to simplify this to: radians * (180 / Pi)
            // See DegreesToRadians for a longer explanation as to why

            return (radians * TSelf.CreateChecked(180)) / TSelf.Pi;
        }

        /// <summary>Computes the sine of a value.</summary>
        /// <param name="x">The value, in radians, whose sine is to be computed.</param>
        /// <returns>The sine of <paramref name="x" />.</returns>
        /// <remarks>This computes <c>sin(x)</c>.</remarks>
        static abstract TSelf Sin(TSelf x);

        /// <summary>Computes the sine and cosine of a value.</summary>
        /// <param name="x">The value, in radians, whose sine and cosine are to be computed.</param>
        /// <returns>The sine and cosine of <paramref name="x" />.</returns>
        /// <remarks>This computes <c>(sin(x), cos(x))</c>.</remarks>
        static abstract (TSelf Sin, TSelf Cos) SinCos(TSelf x);

        /// <summary>Computes the sine and cosine of a value that has been multiplied by <c>pi</c>.</summary>
        /// <param name="x">The value, in half-revolutions, that is multipled by <c>pi</c> before computing its sine and cosine.</param>
        /// <returns>The sine and cosine of<paramref name="x" /> multiplied-by <c>pi</c>.</returns>
        /// <remarks>This computes <c>(sin(x * PI), cos(x * PI))</c>.</remarks>
        static abstract (TSelf SinPi, TSelf CosPi) SinCosPi(TSelf x);

        /// <summary>Computes the sine of a value that has been multiplied by <c>pi</c>.</summary>
        /// <param name="x">The value, in half-revolutions, that is multipled by <c>pi</c> before computing its sine.</param>
        /// <returns>The sine of <paramref name="x" /> multiplied-by <c>pi</c>.</returns>
        /// <remarks>This computes <c>sin(x * PI)</c>.</remarks>
        static abstract TSelf SinPi(TSelf x);

        /// <summary>Computes the tangent of a value.</summary>
        /// <param name="x">The value, in radians, whose tangent is to be computed.</param>
        /// <returns>The tangent of <paramref name="x" />.</returns>
        /// <remarks>This computes <c>tan(x)</c>.</remarks>
        static abstract TSelf Tan(TSelf x);

        /// <summary>Computes the tangent of a value that has been multipled by <c>pi</c>.</summary>
        /// <param name="x">The value, in half-revolutions, that is multipled by <c>pi</c> before computing its tangent.</param>
        /// <returns>The tangent of <paramref name="x" /> multiplied-by <c>pi</c>.</returns>
        /// <remarks>This computes <c>tan(x * PI)</c>.</remarks>
        static abstract TSelf TanPi(TSelf x);
    }
    /// <summary>Defines a mechanism for computing the unary negation of a value.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    /// <typeparam name="TResult">The type that contains the result of negating <typeparamref name="TSelf" />.</typeparam>
    public interface IUnaryNegationOperators<TSelf, TResult>
        where TSelf : IUnaryNegationOperators<TSelf, TResult>?
    {
        /// <summary>Computes the unary negation of a value.</summary>
        /// <param name="value">The value for which to compute its unary negation.</param>
        /// <returns>The unary negation of <paramref name="value" />.</returns>
        static abstract TResult operator -(TSelf value);

        /// <summary>Computes the unary negation of a value.</summary>
        /// <param name="value">The value for which to compute its unary negation.</param>
        /// <returns>The unary negation of <paramref name="value" />.</returns>
        /// <exception cref="OverflowException">The unary negation of <paramref name="value" /> is not representable by <typeparamref name="TResult" />.</exception>
        static virtual TResult operator checked -(TSelf value) => -value;
    }
    /// <summary>Defines a mechanism for computing the unary plus of a value.</summary>
    /// <typeparam name="TSelf">The type that implements this interface.</typeparam>
    /// <typeparam name="TResult">The type that contains the result of converting <typeparamref name="TSelf" />.</typeparam>
    public interface IUnaryPlusOperators<TSelf, TResult>
        where TSelf : IUnaryPlusOperators<TSelf, TResult>?
    {
        /// <summary>Computes the unary plus of a value.</summary>
        /// <param name="value">The value for which to compute its unary plus.</param>
        /// <returns>The unary plus of <paramref name="value" />.</returns>
        static abstract TResult operator +(TSelf value);
    }
    /// <summary>Defines a number type which can only represent positive values, that is it cannot represent negative values.</summary>
    /// <typeparam name="TSelf">The type that implements the interface.</typeparam>
    public interface IUnsignedNumber<TSelf>
        : INumberBase<TSelf>
        where TSelf : IUnsignedNumber<TSelf>?
    {
    }
    /// <summary>Provides a collection of static methods for creating, manipulating, and otherwise operating on generic vectors.</summary>
    [Intrinsic]
    public static partial class Vector
    {
        internal static int Alignment => Vector<byte>.Count;

        /// <summary>Gets a value that indicates whether vector operations are subject to hardware acceleration through JIT intrinsic support.</summary>
        /// <value><see langword="true" /> if vector operations are subject to hardware acceleration; otherwise, <see langword="false" />.</value>
        /// <remarks>Vector operations are subject to hardware acceleration on systems that support single instruction, multiple data (SIMD) instructions and when the RyuJIT just-in-time compiler is used to compile managed code.</remarks>
        public static bool IsHardwareAccelerated
        {
            [Intrinsic]
            get => IsHardwareAccelerated;
        }

        /// <summary>Computes the absolute value of each element in a vector.</summary>
        /// <param name="value">The vector that will have its absolute value computed.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>A vector whose elements are the absolute value of the elements in <paramref name="value" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> Abs<T>(Vector<T> value)
        {
            if ((typeof(T) == typeof(byte))
             || (typeof(T) == typeof(ushort))
             || (typeof(T) == typeof(uint))
             || (typeof(T) == typeof(ulong))
             || (typeof(T) == typeof(nuint)))
            {
                return value;
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T element = Scalar<T>.Abs(value.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, element);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.Add{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        public static Vector<T> Add<T>(Vector<T> left, Vector<T> right) => left + right;

        /// <inheritdoc cref="Vector128.AddSaturate{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> AddSaturate<T>(Vector<T> left, Vector<T> right)
        {
            if ((typeof(T) == typeof(float)) || (typeof(T) == typeof(double)))
            {
                return left + right;
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.AddSaturate(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.All{T}(Vector128{T}, T)" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool All<T>(Vector<T> vector, T value) => vector == Create(value);

        /// <inheritdoc cref="Vector128.AllWhereAllBitsSet{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AllWhereAllBitsSet<T>(Vector<T> vector)
        {
            if (typeof(T) == typeof(float))
            {
                return All(vector.As<T, int>(), -1);
            }
            else if (typeof(T) == typeof(double))
            {
                return All(vector.As<T, long>(), -1);
            }
            else
            {
                return All(vector, Scalar<T>.AllBitsSet);
            }
        }

        /// <summary>Computes the bitwise-and of a given vector and the ones complement of another vector.</summary>
        /// <param name="left">The vector to bitwise-and with <paramref name="right" />.</param>
        /// <param name="right">The vector to that is ones-complemented before being bitwise-and with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>The bitwise-and of <paramref name="left" /> and the ones-complement of <paramref name="right" />.</returns>
        [Intrinsic]
        public static Vector<T> AndNot<T>(Vector<T> left, Vector<T> right) => left & ~right;

        /// <inheritdoc cref="Vector128.Any{T}(Vector128{T}, T)" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Any<T>(Vector<T> vector, T value) => EqualsAny(vector, Create(value));

        /// <inheritdoc cref="Vector128.AnyWhereAllBitsSet{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AnyWhereAllBitsSet<T>(Vector<T> vector)
        {
            if (typeof(T) == typeof(float))
            {
                return Any(vector.As<T, int>(), -1);
            }
            else if (typeof(T) == typeof(double))
            {
                return Any(vector.As<T, long>(), -1);
            }
            else
            {
                return Any(vector, Scalar<T>.AllBitsSet);
            }
        }

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see cref="Vector{U}" />.</summary>
        /// <typeparam name="TFrom">The type of the input vector.</typeparam>
        /// <typeparam name="TTo">The type of the vector <paramref name="vector" /> should be reinterpreted as.</typeparam>
        /// <param name="vector">The vector to reinterpret.</param>
        /// <returns><paramref name="vector" /> reinterpreted as a new <see cref="Vector{U}" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="vector" /> (<typeparamref name="TFrom" />) or the type of the target (<typeparamref name="TTo" />) is not supported.</exception>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<TTo> As<TFrom, TTo>(this Vector<TFrom> vector)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<TFrom>();
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<TTo>();

            return Unsafe.BitCast<Vector<TFrom>, Vector<TTo>>(vector);
        }

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;Byte&gt;" />.</summary>
        /// <typeparam name="T">The type of the input vector.</typeparam>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;Byte&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static Vector<byte> AsVectorByte<T>(Vector<T> value) => value.As<T, byte>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;Double&gt;" />.</summary>
        /// <typeparam name="T">The type of the input vector.</typeparam>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;Double&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static Vector<double> AsVectorDouble<T>(Vector<T> value) => value.As<T, double>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;Int16&gt;" />.</summary>
        /// <typeparam name="T">The type of the input vector.</typeparam>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;Int16&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static Vector<short> AsVectorInt16<T>(Vector<T> value) => value.As<T, short>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;Int32&gt;" />.</summary>
        /// <typeparam name="T">The type of the input vector.</typeparam>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;Int32&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static Vector<int> AsVectorInt32<T>(Vector<T> value) => value.As<T, int>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;Int64&gt;" />.</summary>
        /// <typeparam name="T">The type of the input vector.</typeparam>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;Int64&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static Vector<long> AsVectorInt64<T>(Vector<T> value) => value.As<T, long>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;IntPtr&gt;" />.</summary>
        /// <typeparam name="T">The type of the input vector.</typeparam>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;IntPtr&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static Vector<nint> AsVectorNInt<T>(Vector<T> value) => value.As<T, nint>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;UIntPtr&gt;" />.</summary>
        /// <typeparam name="T">The type of the input vector.</typeparam>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;UIntPtr&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<nuint> AsVectorNUInt<T>(Vector<T> value) => value.As<T, nuint>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;SByte&gt;" />.</summary>
        /// <typeparam name="T">The type of the input vector.</typeparam>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;SByte&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<sbyte> AsVectorSByte<T>(Vector<T> value) => value.As<T, sbyte>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;Single&gt;" />.</summary>
        /// <typeparam name="T">The type of the input vector.</typeparam>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;Single&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static Vector<float> AsVectorSingle<T>(Vector<T> value) => value.As<T, float>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;UInt16&gt;" />.</summary>
        /// <typeparam name="T">The type of the input vector.</typeparam>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;UInt16&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<ushort> AsVectorUInt16<T>(Vector<T> value) => value.As<T, ushort>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;UInt32&gt;" />.</summary>
        /// <typeparam name="T">The type of the input vector.</typeparam>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;UInt32&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<uint> AsVectorUInt32<T>(Vector<T> value) => value.As<T, uint>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;UInt64&gt;" />.</summary>
        /// <typeparam name="T">The type of the input vector.</typeparam>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;UInt64&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<ulong> AsVectorUInt64<T>(Vector<T> value) => value.As<T, ulong>();

        /// <summary>Computes the bitwise-and of two vectors.</summary>
        /// <param name="left">The vector to bitwise-and with <paramref name="right" />.</param>
        /// <param name="right">The vector to bitwise-and with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>The bitwise-and of <paramref name="left" /> and <paramref name="right" />.</returns>
        [Intrinsic]
        public static Vector<T> BitwiseAnd<T>(Vector<T> left, Vector<T> right) => left & right;

        /// <summary>Computes the bitwise-or of two vectors.</summary>
        /// <param name="left">The vector to bitwise-or with <paramref name="right" />.</param>
        /// <param name="right">The vector to bitwise-or with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>The bitwise-or of <paramref name="left" /> and <paramref name="right" />.</returns>
        [Intrinsic]
        public static Vector<T> BitwiseOr<T>(Vector<T> left, Vector<T> right) => left | right;

        /// <summary>Computes the ceiling of each element in a vector.</summary>
        /// <param name="vector">The vector that will have its ceiling computed.</param>
        /// <returns>A vector whose elements are the ceiling of the elements in <paramref name="vector" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector<T> Ceiling<T>(Vector<T> vector)
        {
            if ((typeof(T) == typeof(byte))
             || (typeof(T) == typeof(short))
             || (typeof(T) == typeof(int))
             || (typeof(T) == typeof(long))
             || (typeof(T) == typeof(nint))
             || (typeof(T) == typeof(nuint))
             || (typeof(T) == typeof(sbyte))
             || (typeof(T) == typeof(ushort))
             || (typeof(T) == typeof(uint))
             || (typeof(T) == typeof(ulong)))
            {
                return vector;
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.Ceiling(vector.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <summary>Computes the ceiling of each element in a vector.</summary>
        /// <param name="value">The vector that will have its ceiling computed.</param>
        /// <returns>A vector whose elements are the ceiling of the elements in <paramref name="value" />.</returns>
        /// <seealso cref="double.Ceiling(double)" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> Ceiling(Vector<double> value)
        {
            Unsafe.SkipInit(out Vector<double> result);

            for (int index = 0; index < Vector<double>.Count; index++)
            {
                double element = Scalar<double>.Ceiling(value.GetElementUnsafe(index));
                result.SetElementUnsafe(index, element);
            }

            return result;
        }

        /// <summary>Computes the ceiling of each element in a vector.</summary>
        /// <param name="value">The vector that will have its ceiling computed.</param>
        /// <returns>A vector whose elements are the ceiling of the elements in <paramref name="value" />.</returns>
        /// <seealso cref="float.Ceiling(float)" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> Ceiling(Vector<float> value)
        {
            Unsafe.SkipInit(out Vector<float> result);

            for (int index = 0; index < Vector<float>.Count; index++)
            {
                float element = Scalar<float>.Ceiling(value.GetElementUnsafe(index));
                result.SetElementUnsafe(index, element);
            }

            return result;
        }

        /// <inheritdoc cref="Vector128.Clamp{T}(Vector128{T}, Vector128{T}, Vector128{T})" />
        [Intrinsic]
        public static Vector<T> Clamp<T>(Vector<T> value, Vector<T> min, Vector<T> max)
        {
            // We must follow HLSL behavior in the case user specified min value is bigger than max value.
            return Min(Max(value, min), max);
        }

        /// <inheritdoc cref="Vector128.ClampNative{T}(Vector128{T}, Vector128{T}, Vector128{T})" />
        [Intrinsic]
        public static Vector<T> ClampNative<T>(Vector<T> value, Vector<T> min, Vector<T> max)
        {
            // We must follow HLSL behavior in the case user specified min value is bigger than max value.
            return MinNative(MaxNative(value, min), max);
        }

        /// <summary>Conditionally selects a value from two vectors on a bitwise basis.</summary>
        /// <param name="condition">The mask that is used to select a value from <paramref name="left" /> or <paramref name="right" />.</param>
        /// <param name="left">The vector that is selected when the corresponding bit in <paramref name="condition" /> is one.</param>
        /// <param name="right">The vector that is selected when the corresponding bit in <paramref name="condition" /> is zero.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>A vector whose bits come from <paramref name="left" /> or <paramref name="right" /> based on the value of <paramref name="condition" />.</returns>
        /// <remarks>The returned vector is equivalent to <paramref name="condition" /> <c>?</c> <paramref name="left" /> <c>:</c> <paramref name="right" /> on a per-bit basis.</remarks>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> ConditionalSelect<T>(Vector<T> condition, Vector<T> left, Vector<T> right) => (left & condition) | AndNot(right, condition);

        /// <summary>Conditionally selects a value from two vectors on a bitwise basis.</summary>
        /// <param name="condition">The mask that is used to select a value from <paramref name="left" /> or <paramref name="right" />.</param>
        /// <param name="left">The vector that is selected when the corresponding bit in <paramref name="condition" /> is one.</param>
        /// <param name="right">The vector that is selected when the corresponding bit in <paramref name="condition" /> is zero.</param>
        /// <returns>A vector whose bits come from <paramref name="left" /> or <paramref name="right" /> based on the value of <paramref name="condition" />.</returns>
        /// <remarks>The returned vector is equivalent to <paramref name="condition" /> <c>?</c> <paramref name="left" /> <c>:</c> <paramref name="right" /> on a per-bit basis.</remarks>
        [Intrinsic]
        public static Vector<float> ConditionalSelect(Vector<int> condition, Vector<float> left, Vector<float> right) => ConditionalSelect(condition.As<int, float>(), left, right);

        /// <summary>Conditionally selects a value from two vectors on a bitwise basis.</summary>
        /// <param name="condition">The mask that is used to select a value from <paramref name="left" /> or <paramref name="right" />.</param>
        /// <param name="left">The vector that is selected when the corresponding bit in <paramref name="condition" /> is one.</param>
        /// <param name="right">The vector that is selected when the corresponding bit in <paramref name="condition" /> is zero.</param>
        /// <returns>A vector whose bits come from <paramref name="left" /> or <paramref name="right" /> based on the value of <paramref name="condition" />.</returns>
        /// <remarks>The returned vector is equivalent to <paramref name="condition" /> <c>?</c> <paramref name="left" /> <c>:</c> <paramref name="right" /> on a per-bit basis.</remarks>
        [Intrinsic]
        public static Vector<double> ConditionalSelect(Vector<long> condition, Vector<double> left, Vector<double> right) => ConditionalSelect(condition.As<long, double>(), left, right);

        /// <summary>Converts a <see langword="Vector&lt;Int64&gt;" /> to a <see langword="Vector&lt;Double&gt;" />.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns>The converted vector.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> ConvertToDouble(Vector<long> value)
        {
            if (Vector<double>.Count == Vector512<double>.Count)
            {
                return Vector512.ConvertToDouble(value.AsVector512()).AsVector();
            }
            else if (Vector<double>.Count == Vector256<double>.Count)
            {
                return Vector256.ConvertToDouble(value.AsVector256()).AsVector();
            }
            else
            {
                return Vector128.ConvertToDouble(value.AsVector128()).AsVector();
            }
        }

        /// <summary>Converts a <see langword="Vector&lt;UInt64&gt;" /> to a <see langword="Vector&lt;Double&gt;" />.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns>The converted vector.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> ConvertToDouble(Vector<ulong> value)
        {
            if (Vector<double>.Count == Vector512<double>.Count)
            {
                return Vector512.ConvertToDouble(value.AsVector512()).AsVector();
            }
            else if (Vector<double>.Count == Vector256<double>.Count)
            {
                return Vector256.ConvertToDouble(value.AsVector256()).AsVector();
            }
            else
            {
                return Vector128.ConvertToDouble(value.AsVector128()).AsVector();
            }
        }

        /// <summary>Converts a <see langword="Vector&lt;Single&gt;" /> to a <see langword="Vector&lt;Int32&gt;" /> using saturation on overflow.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns>The converted vector.</returns>
        [Intrinsic]
        public static Vector<int> ConvertToInt32(Vector<float> value)
        {
            Unsafe.SkipInit(out Vector<int> result);

            for (int i = 0; i < Vector<int>.Count; i++)
            {
                int element = float.ConvertToInteger<int>(value.GetElementUnsafe(i));
                result.SetElementUnsafe(i, element);
            }

            return result;
        }

        /// <summary>Converts a <see langword="Vector&lt;Single&gt;" /> to a <see langword="Vector&lt;Int32&gt;" /> using platform specific behavior on overflow.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns>The converted vector.</returns>
        [Intrinsic]
        public static Vector<int> ConvertToInt32Native(Vector<float> value)
        {
            Unsafe.SkipInit(out Vector<int> result);

            for (int i = 0; i < Vector<int>.Count; i++)
            {
                int element = float.ConvertToIntegerNative<int>(value.GetElementUnsafe(i));
                result.SetElementUnsafe(i, element);
            }

            return result;
        }

        /// <summary>Converts a <see langword="Vector&lt;Double&gt;" /> to a <see langword="Vector&lt;Int64&gt;" /> using saturation on overflow.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns>The converted vector.</returns>
        [Intrinsic]
        public static Vector<long> ConvertToInt64(Vector<double> value)
        {
            Unsafe.SkipInit(out Vector<long> result);

            for (int i = 0; i < Vector<long>.Count; i++)
            {
                long element = double.ConvertToInteger<long>(value.GetElementUnsafe(i));
                result.SetElementUnsafe(i, element);
            }

            return result;
        }

        /// <summary>Converts a <see langword="Vector&lt;Double&gt;" /> to a <see langword="Vector&lt;Int64&gt;" /> using platform specific behavior on overflow.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns>The converted vector.</returns>
        [Intrinsic]
        public static Vector<long> ConvertToInt64Native(Vector<double> value)
        {
            Unsafe.SkipInit(out Vector<long> result);

            for (int i = 0; i < Vector<long>.Count; i++)
            {
                long element = double.ConvertToIntegerNative<long>(value.GetElementUnsafe(i));
                result.SetElementUnsafe(i, element);
            }

            return result;
        }

        /// <summary>Converts a <see langword="Vector&lt;Int32&gt;" /> to a <see langword="Vector&lt;Single&gt;" />.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns>The converted vector.</returns>
        [Intrinsic]
        public static Vector<float> ConvertToSingle(Vector<int> value)
        {
            Unsafe.SkipInit(out Vector<float> result);

            for (int i = 0; i < Vector<float>.Count; i++)
            {
                float element = value.GetElementUnsafe(i);
                result.SetElementUnsafe(i, element);
            }

            return result;
        }

        /// <summary>Converts a <see langword="Vector&lt;UInt32&gt;" /> to a <see langword="Vector&lt;Single&gt;" />.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns>The converted vector.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> ConvertToSingle(Vector<uint> value)
        {
            if (Vector<float>.Count == Vector512<float>.Count)
            {
                return Vector512.ConvertToSingle(value.AsVector512()).AsVector();
            }
            else if (Vector<float>.Count == Vector256<float>.Count)
            {
                return Vector256.ConvertToSingle(value.AsVector256()).AsVector();
            }
            else
            {
                return Vector128.ConvertToSingle(value.AsVector128()).AsVector();
            }
        }

        /// <summary>Converts a <see langword="Vector&lt;Single&gt;" /> to a <see langword="Vector&lt;UInt32&gt;" /> using saturation on overflow.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns>The converted vector.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<uint> ConvertToUInt32(Vector<float> value)
        {
            Unsafe.SkipInit(out Vector<uint> result);

            for (int i = 0; i < Vector<uint>.Count; i++)
            {
                uint element = float.ConvertToInteger<uint>(value.GetElementUnsafe(i));
                result.SetElementUnsafe(i, element);
            }

            return result;
        }

        /// <summary>Converts a <see langword="Vector&lt;Single&gt;" /> to a <see langword="Vector&lt;UInt32&gt;" /> using platform specific behavior on overflow.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns>The converted vector.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<uint> ConvertToUInt32Native(Vector<float> value)
        {
            Unsafe.SkipInit(out Vector<uint> result);

            for (int i = 0; i < Vector<uint>.Count; i++)
            {
                uint element = float.ConvertToIntegerNative<uint>(value.GetElementUnsafe(i));
                result.SetElementUnsafe(i, element);
            }

            return result;
        }

        /// <summary>Converts a <see langword="Vector&lt;Double&gt;" /> to a <see langword="Vector&lt;UInt64&gt;" /> using saturation on overflow.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns>The converted vector.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<ulong> ConvertToUInt64(Vector<double> value)
        {
            Unsafe.SkipInit(out Vector<ulong> result);

            for (int i = 0; i < Vector<ulong>.Count; i++)
            {
                ulong element = double.ConvertToInteger<ulong>(value.GetElementUnsafe(i));
                result.SetElementUnsafe(i, element);
            }

            return result;
        }

        /// <summary>Converts a <see langword="Vector&lt;Double&gt;" /> to a <see langword="Vector&lt;UInt64&gt;" /> using platform specific behavior on overflow.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns>The converted vector.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<ulong> ConvertToUInt64Native(Vector<double> value)
        {
            Unsafe.SkipInit(out Vector<ulong> result);

            for (int i = 0; i < Vector<ulong>.Count; i++)
            {
                ulong element = double.ConvertToIntegerNative<ulong>(value.GetElementUnsafe(i));
                result.SetElementUnsafe(i, element);
            }

            return result;
        }

        internal static Vector<T> Cos<T>(Vector<T> vector)
            where T : ITrigonometricFunctions<T>
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = T.Cos(vector.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <inheritdoc cref="Vector128.Cos(Vector128{double})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> Cos(Vector<double> vector)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.CosDouble<Vector<double>, Vector<long>>(vector);
            }
            else
            {
                return Cos<double>(vector);
            }
        }

        /// <inheritdoc cref="Vector128.Cos(Vector128{float})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> Cos(Vector<float> vector)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.CosSingle<Vector<float>, Vector<int>, Vector<double>, Vector<long>>(vector);
            }
            else
            {
                return Cos<float>(vector);
            }
        }

        /// <inheritdoc cref="Vector128.CopySign{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> CopySign<T>(Vector<T> value, Vector<T> sign)
        {
            if ((typeof(T) == typeof(byte))
             || (typeof(T) == typeof(ushort))
             || (typeof(T) == typeof(uint))
             || (typeof(T) == typeof(ulong))
             || (typeof(T) == typeof(nuint)))
            {
                return value;
            }
            else if (IsHardwareAccelerated)
            {
                return VectorMath.CopySign<Vector<T>, T>(value, sign);
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T element = Scalar<T>.CopySign(value.GetElementUnsafe(index), sign.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, element);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.Count{T}(Vector128{T}, T)" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Count<T>(Vector<T> vector, T value)
        {
            if (Vector<T>.Count == Vector512<T>.Count)
            {
                return Vector512.Count(vector.AsVector512(), value);
            }
            else if (Vector<T>.Count == Vector256<T>.Count)
            {
                return Vector256.Count(vector.AsVector256(), value);
            }
            else
            {
                return Vector128.Count(vector.AsVector128(), value);
            }
        }

        /// <inheritdoc cref="Vector128.CountWhereAllBitsSet{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountWhereAllBitsSet<T>(Vector<T> vector)
        {
            if (typeof(T) == typeof(float))
            {
                return Count(vector.As<T, int>(), -1);
            }
            else if (typeof(T) == typeof(double))
            {
                return Count(vector.As<T, long>(), -1);
            }
            else
            {
                return Count(vector, Scalar<T>.AllBitsSet);
            }
        }

        /// <summary>Creates a new <see cref="Vector{T}" /> instance with all elements initialized to the specified value.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="value">The value that all elements will be initialized to.</param>
        /// <returns>A new <see cref="Vector{T}" /> with all elements initialized to <paramref name="value" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static Vector<T> Create<T>(T value)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Creates a new <see cref="Vector{T}" /> from a given readonly span.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="values">The readonly span from which the vector is created.</param>
        /// <returns>A new <see cref="Vector{T}" /> with its elements set to the first <see cref="Vector{T}.Count" /> elements from <paramref name="values" />.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The length of <paramref name="values" /> is less than <see cref="Vector128{T}.Count" />.</exception>
        /// <exception cref="NotSupportedException">The type of <paramref name="values" /> (<typeparamref name="T" />) is not supported.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> Create<T>(ReadOnlySpan<T> values)
        {
            if (values.Length < Vector<T>.Count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.values);
            }
            return Unsafe.ReadUnaligned<Vector<T>>(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(values)));
        }

        /// <summary>Creates a new <see cref="Vector{T}" /> instance with the first element initialized to the specified value and the remaining elements initialized to zero.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="value">The value that element 0 will be initialized to.</param>
        /// <returns>A new <see cref="Vector{T}" /> instance with the first element initialized to <paramref name="value" /> and the remaining elements initialized to zero.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static Vector<T> CreateScalar<T>(T value)
        {
            Vector<T> result = Vector<T>.Zero;
            result.SetElementUnsafe(0, value);
            return result;
        }

        /// <summary>Creates a new <see cref="Vector{T}" /> instance with the first element initialized to the specified value and the remaining elements left uninitialized.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="value">The value that element 0 will be initialized to.</param>
        /// <returns>A new <see cref="Vector{T}" /> instance with the first element initialized to <paramref name="value" /> and the remaining elements left uninitialized.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> CreateScalarUnsafe<T>(T value)
        {
            // This relies on us stripping the "init" flag from the ".locals"
            // declaration to let the upper bits be uninitialized.

            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
            Unsafe.SkipInit(out Vector<T> result);

            result.SetElementUnsafe(0, value);
            return result;
        }

        /// <summary>Creates a new <see cref="Vector{T}" /> instance where the elements begin at a specified value and which are spaced apart according to another specified value.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="start">The value that element 0 will be initialized to.</param>
        /// <param name="step">The value that indicates how far apart each element should be from the previous.</param>
        /// <returns>A new <see cref="Vector{T}" /> instance with the first element initialized to <paramref name="start" /> and each subsequent element initialized to the value of the previous element plus <paramref name="step" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> CreateSequence<T>(T start, T step) => (Vector<T>.Indices * step) + Create(start);

        internal static Vector<T> DegreesToRadians<T>(Vector<T> degrees)
            where T : ITrigonometricFunctions<T>
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = T.DegreesToRadians(degrees.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <inheritdoc cref="Vector128.DegreesToRadians(Vector128{double})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> DegreesToRadians(Vector<double> degrees)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.DegreesToRadians<Vector<double>, double>(degrees);
            }
            else
            {
                return DegreesToRadians<double>(degrees);
            }
        }

        /// <inheritdoc cref="Vector128.DegreesToRadians(Vector128{float})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> DegreesToRadians(Vector<float> degrees)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.DegreesToRadians<Vector<float>, float>(degrees);
            }
            else
            {
                return DegreesToRadians<float>(degrees);
            }
        }

        /// <summary>Divides two vectors to compute their quotient.</summary>
        /// <param name="left">The vector that will be divided by <paramref name="right" />.</param>
        /// <param name="right">The vector that will divide <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>The quotient of <paramref name="left" /> divided by <paramref name="right" />.</returns>
        [Intrinsic]
        public static Vector<T> Divide<T>(Vector<T> left, Vector<T> right) => left / right;

        /// <summary>Divides a vector by a scalar to compute the per-element quotient.</summary>
        /// <param name="left">The vector that will be divided by <paramref name="right" />.</param>
        /// <param name="right">The scalar that will divide <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>The quotient of <paramref name="left" /> divided by <paramref name="right" />.</returns>
        [Intrinsic]
        public static Vector<T> Divide<T>(Vector<T> left, T right) => left / right;

        /// <summary>Computes the dot product of two vectors.</summary>
        /// <param name="left">The vector that will be dotted with <paramref name="right" />.</param>
        /// <param name="right">The vector that will be dotted with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>The dot product of <paramref name="left" /> and <paramref name="right" />.</returns>
        [Intrinsic]
        public static T Dot<T>(Vector<T> left, Vector<T> right) => Sum(left * right);

        /// <summary>Compares two vectors to determine if they are equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were equal.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> Equals<T>(Vector<T> left, Vector<T> right)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = Scalar<T>.Equals(left.GetElementUnsafe(index), right.GetElementUnsafe(index)) ? Scalar<T>.AllBitsSet : default!;
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Compares two vectors to determine if they are equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were equal.</returns>
        [Intrinsic]
        public static Vector<long> Equals(Vector<double> left, Vector<double> right) => Equals<double>(left, right).As<double, long>();

        /// <summary>Compares two vectors to determine if they are equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were equal.</returns>
        [Intrinsic]
        public static Vector<int> Equals(Vector<int> left, Vector<int> right) => Equals<int>(left, right);

        /// <summary>Compares two vectors to determine if they are equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were equal.</returns>
        [Intrinsic]
        public static Vector<long> Equals(Vector<long> left, Vector<long> right) => Equals<long>(left, right);

        /// <summary>Compares two vectors to determine if they are equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were equal.</returns>
        [Intrinsic]
        public static Vector<int> Equals(Vector<float> left, Vector<float> right) => Equals<float>(left, right).As<float, int>();

        /// <summary>Compares two vectors to determine if all elements are equal.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns><c>true</c> if all elements in <paramref name="left" /> were equal to the corresponding element in <paramref name="right" />.</returns>
        [Intrinsic]
        public static bool EqualsAll<T>(Vector<T> left, Vector<T> right) => left == right;

        /// <summary>Compares two vectors to determine if any elements are equal.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns><c>true</c> if any elements in <paramref name="left" /> was equal to the corresponding element in <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(Vector<T> left, Vector<T> right)
        {
            for (int index = 0; index < Vector<T>.Count; index++)
            {
                if (Scalar<T>.Equals(left.GetElementUnsafe(index), right.GetElementUnsafe(index)))
                {
                    return true;
                }
            }

            return false;
        }

        internal static Vector<T> Exp<T>(Vector<T> vector)
            where T : IExponentialFunctions<T>
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = T.Exp(vector.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <inheritdoc cref="Vector128.Exp(Vector128{double})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> Exp(Vector<double> vector)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.ExpDouble<Vector<double>, Vector<ulong>>(vector);
            }
            else
            {
                return Exp<double>(vector);
            }
        }

        /// <inheritdoc cref="Vector128.Exp(Vector128{float})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> Exp(Vector<float> vector)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.ExpSingle<Vector<float>, Vector<uint>, Vector<double>, Vector<ulong>>(vector);
            }
            else
            {
                return Exp<float>(vector);
            }
        }

        /// <summary>Computes the floor of each element in a vector.</summary>
        /// <param name="vector">The vector that will have its floor computed.</param>
        /// <returns>A vector whose elements are the floor of the elements in <paramref name="vector" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector<T> Floor<T>(Vector<T> vector)
        {
            if ((typeof(T) == typeof(byte))
             || (typeof(T) == typeof(short))
             || (typeof(T) == typeof(int))
             || (typeof(T) == typeof(long))
             || (typeof(T) == typeof(nint))
             || (typeof(T) == typeof(nuint))
             || (typeof(T) == typeof(sbyte))
             || (typeof(T) == typeof(ushort))
             || (typeof(T) == typeof(uint))
             || (typeof(T) == typeof(ulong)))
            {
                return vector;
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.Floor(vector.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <summary>Computes the floor of each element in a vector.</summary>
        /// <param name="value">The vector that will have its floor computed.</param>
        /// <returns>A vector whose elements are the floor of the elements in <paramref name="value" />.</returns>
        /// <seealso cref="double.Floor(double)" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> Floor(Vector<double> value)
        {
            Unsafe.SkipInit(out Vector<double> result);

            for (int index = 0; index < Vector<double>.Count; index++)
            {
                double element = Scalar<double>.Floor(value.GetElementUnsafe(index));
                result.SetElementUnsafe(index, element);
            }

            return result;
        }

        /// <summary>Computes the floor of each element in a vector.</summary>
        /// <param name="value">The vector that will have its floor computed.</param>
        /// <returns>A vector whose elements are the floor of the elements in <paramref name="value" />.</returns>
        /// <seealso cref="float.Floor(float)" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> Floor(Vector<float> value)
        {
            Unsafe.SkipInit(out Vector<float> result);

            for (int index = 0; index < Vector<float>.Count; index++)
            {
                float element = Scalar<float>.Floor(value.GetElementUnsafe(index));
                result.SetElementUnsafe(index, element);
            }

            return result;
        }

        /// <summary>Computes (<paramref name="left" /> * <paramref name="right" />) + <paramref name="addend" />, rounded as one ternary operation.</summary>
        /// <param name="left">The vector to be multiplied with <paramref name="right" />.</param>
        /// <param name="right">The vector to be multiplied with <paramref name="left" />.</param>
        /// <param name="addend">The vector to be added to the result of <paramref name="left" /> multiplied by <paramref name="right" />.</param>
        /// <returns>(<paramref name="left" /> * <paramref name="right" />) + <paramref name="addend" />, rounded as one ternary operation.</returns>
        /// <remarks>
        ///   <para>This computes (<paramref name="left" /> * <paramref name="right" />) as if to infinite precision, adds <paramref name="addend" /> to that result as if to infinite precision, and finally rounds to the nearest representable value.</para>
        ///   <para>This differs from the non-fused sequence which would compute (<paramref name="left" /> * <paramref name="right" />) as if to infinite precision, round the result to the nearest representable value, add <paramref name="addend" /> to the rounded result as if to infinite precision, and finally round to the nearest representable value.</para>
        /// </remarks>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> FusedMultiplyAdd(Vector<double> left, Vector<double> right, Vector<double> addend)
        {
            Unsafe.SkipInit(out Vector<double> result);

            for (int index = 0; index < Vector<double>.Count; index++)
            {
                double value = double.FusedMultiplyAdd(left.GetElementUnsafe(index), right.GetElementUnsafe(index), addend.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Computes (<paramref name="left" /> * <paramref name="right" />) + <paramref name="addend" />, rounded as one ternary operation.</summary>
        /// <param name="left">The vector to be multiplied with <paramref name="right" />.</param>
        /// <param name="right">The vector to be multiplied with <paramref name="left" />.</param>
        /// <param name="addend">The vector to be added to the result of <paramref name="left" /> multiplied by <paramref name="right" />.</param>
        /// <returns>(<paramref name="left" /> * <paramref name="right" />) + <paramref name="addend" />, rounded as one ternary operation.</returns>
        /// <remarks>
        ///   <para>This computes (<paramref name="left" /> * <paramref name="right" />) as if to infinite precision, adds <paramref name="addend" /> to that result as if to infinite precision, and finally rounds to the nearest representable value.</para>
        ///   <para>This differs from the non-fused sequence which would compute (<paramref name="left" /> * <paramref name="right" />) as if to infinite precision, round the result to the nearest representable value, add <paramref name="addend" /> to the rounded result as if to infinite precision, and finally round to the nearest representable value.</para>
        /// </remarks>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> FusedMultiplyAdd(Vector<float> left, Vector<float> right, Vector<float> addend)
        {
            Unsafe.SkipInit(out Vector<float> result);

            for (int index = 0; index < Vector<float>.Count; index++)
            {
                float value = float.FusedMultiplyAdd(left.GetElementUnsafe(index), right.GetElementUnsafe(index), addend.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Gets the element at the specified index.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="vector">The vector to get the element from.</param>
        /// <param name="index">The index of the element to get.</param>
        /// <returns>The value of the element at <paramref name="index" />.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> was less than zero or greater than the number of elements.</exception>
        /// <exception cref="NotSupportedException">The type of <paramref name="vector" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T GetElement<T>(this Vector<T> vector, int index)
        {
            if ((uint)(index) >= (uint)(Vector<T>.Count))
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.index);
            }

            return vector.GetElementUnsafe(index);
        }

        /// <summary>Compares two vectors to determine which is greater on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were greater.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> GreaterThan<T>(Vector<T> left, Vector<T> right)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = Scalar<T>.GreaterThan(left.GetElementUnsafe(index), right.GetElementUnsafe(index)) ? Scalar<T>.AllBitsSet : default!;
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Compares two vectors to determine which is greater on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were greater.</returns>
        [Intrinsic]
        public static Vector<long> GreaterThan(Vector<double> left, Vector<double> right) => GreaterThan<double>(left, right).As<double, long>();

        /// <summary>Compares two vectors to determine which is greater on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were greater.</returns>
        [Intrinsic]
        public static Vector<int> GreaterThan(Vector<int> left, Vector<int> right) => GreaterThan<int>(left, right);

        /// <summary>Compares two vectors to determine which is greater on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were greater.</returns>
        [Intrinsic]
        public static Vector<long> GreaterThan(Vector<long> left, Vector<long> right) => GreaterThan<long>(left, right);

        /// <summary>Compares two vectors to determine which is greater on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were greater.</returns>
        [Intrinsic]
        public static Vector<int> GreaterThan(Vector<float> left, Vector<float> right) => GreaterThan<float>(left, right).As<float, int>();

        /// <summary>Compares two vectors to determine if all elements are greater.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns><c>true</c> if all elements in <paramref name="left" /> were greater than the corresponding element in <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GreaterThanAll<T>(Vector<T> left, Vector<T> right)
        {
            for (int index = 0; index < Vector<T>.Count; index++)
            {
                if (!Scalar<T>.GreaterThan(left.GetElementUnsafe(index), right.GetElementUnsafe(index)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Compares two vectors to determine if any elements are greater.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns><c>true</c> if any elements in <paramref name="left" /> was greater than the corresponding element in <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GreaterThanAny<T>(Vector<T> left, Vector<T> right)
        {
            for (int index = 0; index < Vector<T>.Count; index++)
            {
                if (Scalar<T>.GreaterThan(left.GetElementUnsafe(index), right.GetElementUnsafe(index)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Compares two vectors to determine which is greater or equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were greater or equal.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> GreaterThanOrEqual<T>(Vector<T> left, Vector<T> right)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = Scalar<T>.GreaterThanOrEqual(left.GetElementUnsafe(index), right.GetElementUnsafe(index)) ? Scalar<T>.AllBitsSet : default!;
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Compares two vectors to determine which is greater or equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were greater or equal.</returns>
        [Intrinsic]
        public static Vector<long> GreaterThanOrEqual(Vector<double> left, Vector<double> right) => GreaterThanOrEqual<double>(left, right).As<double, long>();

        /// <summary>Compares two vectors to determine which is greater or equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were greater or equal.</returns>
        [Intrinsic]
        public static Vector<int> GreaterThanOrEqual(Vector<int> left, Vector<int> right) => GreaterThanOrEqual<int>(left, right);

        /// <summary>Compares two vectors to determine which is greater or equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were greater or equal.</returns>
        [Intrinsic]
        public static Vector<long> GreaterThanOrEqual(Vector<long> left, Vector<long> right) => GreaterThanOrEqual<long>(left, right);

        /// <summary>Compares two vectors to determine which is greater or equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were greater or equal.</returns>
        [Intrinsic]
        public static Vector<int> GreaterThanOrEqual(Vector<float> left, Vector<float> right) => GreaterThanOrEqual<float>(left, right).As<float, int>();

        /// <summary>Compares two vectors to determine if all elements are greater or equal.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns><c>true</c> if all elements in <paramref name="left" /> were greater than or equal to the corresponding element in <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GreaterThanOrEqualAll<T>(Vector<T> left, Vector<T> right)
        {
            for (int index = 0; index < Vector<T>.Count; index++)
            {
                if (!Scalar<T>.GreaterThanOrEqual(left.GetElementUnsafe(index), right.GetElementUnsafe(index)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Compares two vectors to determine if any elements are greater or equal.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns><c>true</c> if any elements in <paramref name="left" /> was greater than or equal to the corresponding element in <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GreaterThanOrEqualAny<T>(Vector<T> left, Vector<T> right)
        {
            for (int index = 0; index < Vector<T>.Count; index++)
            {
                if (Scalar<T>.GreaterThanOrEqual(left.GetElementUnsafe(index), right.GetElementUnsafe(index)))
                {
                    return true;
                }
            }

            return false;
        }

        internal static Vector<T> Hypot<T>(Vector<T> x, Vector<T> y)
            where T : IRootFunctions<T>
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = T.Hypot(x.GetElementUnsafe(index), y.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <inheritdoc cref="Vector128.Hypot(Vector128{double}, Vector128{double})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> Hypot(Vector<double> x, Vector<double> y)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.HypotDouble<Vector<double>, Vector<ulong>>(x, y);
            }
            else
            {
                return Hypot<double>(x, y);
            }
        }

        /// <inheritdoc cref="Vector128.Hypot(Vector128{float}, Vector128{float})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> Hypot(Vector<float> x, Vector<float> y)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.HypotSingle<Vector<float>, Vector<double>>(x, y);
            }
            else
            {
                return Hypot<float>(x, y);
            }
        }

        /// <inheritdoc cref="Vector128.IndexOf{T}(Vector128{T}, T)" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf<T>(Vector<T> vector, T value)
        {
            if (Vector<T>.Count == Vector512<T>.Count)
            {
                return Vector512.IndexOf(vector.AsVector512(), value);
            }
            else if (Vector<T>.Count == Vector256<T>.Count)
            {
                return Vector256.IndexOf(vector.AsVector256(), value);
            }
            else
            {
                return Vector128.IndexOf(vector.AsVector128(), value);
            }
        }

        /// <inheritdoc cref="Vector128.IndexOfWhereAllBitsSet{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfWhereAllBitsSet<T>(Vector<T> vector)
        {
            if (typeof(T) == typeof(float))
            {
                return IndexOf(vector.As<T, int>(), -1);
            }
            else if (typeof(T) == typeof(double))
            {
                return IndexOf(vector.As<T, long>(), -1);
            }
            else
            {
                return IndexOf(vector, Scalar<T>.AllBitsSet);
            }
        }

        /// <inheritdoc cref="Vector128.IsEvenInteger{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> IsEvenInteger<T>(Vector<T> vector)
        {
            if (typeof(T) == typeof(float))
            {
                return VectorMath.IsEvenIntegerSingle<Vector<float>, Vector<uint>>(vector.As<T, float>()).As<float, T>();
            }
            else if (typeof(T) == typeof(double))
            {
                return VectorMath.IsEvenIntegerDouble<Vector<double>, Vector<ulong>>(vector.As<T, double>()).As<double, T>();
            }
            return IsZero(vector & Vector<T>.One);
        }

        /// <inheritdoc cref="Vector128.IsFinite{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> IsFinite<T>(Vector<T> vector)
        {
            if (typeof(T) == typeof(float))
            {
                return ~IsZero(AndNot(Create<uint>(float.PositiveInfinityBits), vector.As<T, uint>())).As<uint, T>();
            }
            else if (typeof(T) == typeof(double))
            {
                return ~IsZero(AndNot(Create<ulong>(double.PositiveInfinityBits), vector.As<T, ulong>())).As<ulong, T>();
            }
            return Vector<T>.AllBitsSet;
        }

        /// <inheritdoc cref="Vector128.IsInfinity{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> IsInfinity<T>(Vector<T> vector)
        {
            if ((typeof(T) == typeof(float)) || (typeof(T) == typeof(double)))
            {
                return IsPositiveInfinity(Abs(vector));
            }
            return Vector<T>.Zero;
        }

        /// <inheritdoc cref="Vector128.IsInteger{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> IsInteger<T>(Vector<T> vector)
        {
            if ((typeof(T) == typeof(float)) || (typeof(T) == typeof(double)))
            {
                return IsFinite(vector) & Equals(vector, Truncate(vector));
            }
            return Vector<T>.AllBitsSet;
        }

        /// <inheritdoc cref="Vector128.IsNaN{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> IsNaN<T>(Vector<T> vector)
        {
            if ((typeof(T) == typeof(float)) || (typeof(T) == typeof(double)))
            {
                return ~Equals(vector, vector);
            }
            return Vector<T>.Zero;
        }

        /// <inheritdoc cref="Vector128.IsNegative{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> IsNegative<T>(Vector<T> vector)
        {
            if ((typeof(T) == typeof(byte))
             || (typeof(T) == typeof(ushort))
             || (typeof(T) == typeof(uint))
             || (typeof(T) == typeof(ulong))
             || (typeof(T) == typeof(nuint)))
            {
                return Vector<T>.Zero;
            }
            else if (typeof(T) == typeof(float))
            {
                return LessThan(vector.As<T, int>(), Vector<int>.Zero).As<int, T>();
            }
            else if (typeof(T) == typeof(double))
            {
                return LessThan(vector.As<T, long>(), Vector<long>.Zero).As<long, T>();
            }
            else
            {
                return LessThan(vector, Vector<T>.Zero);
            }
        }

        /// <inheritdoc cref="Vector128.IsNegativeInfinity{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> IsNegativeInfinity<T>(Vector<T> vector)
        {
            if (typeof(T) == typeof(float))
            {
                return Equals(vector, Create(float.NegativeInfinity).As<float, T>());
            }
            else if (typeof(T) == typeof(double))
            {
                return Equals(vector, Create(double.NegativeInfinity).As<double, T>());
            }
            return Vector<T>.Zero;
        }

        /// <inheritdoc cref="Vector128.IsNormal{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> IsNormal<T>(Vector<T> vector)
        {
            if (typeof(T) == typeof(float))
            {
                return LessThan(Abs(vector).As<T, uint>() - Create<uint>(float.SmallestNormalBits), Create<uint>(float.PositiveInfinityBits - float.SmallestNormalBits)).As<uint, T>();
            }
            else if (typeof(T) == typeof(double))
            {
                return LessThan(Abs(vector).As<T, ulong>() - Create<ulong>(double.SmallestNormalBits), Create<ulong>(double.PositiveInfinityBits - double.SmallestNormalBits)).As<ulong, T>();
            }
            return ~IsZero(vector);
        }

        /// <inheritdoc cref="Vector128.IsOddInteger{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> IsOddInteger<T>(Vector<T> vector)
        {
            if (typeof(T) == typeof(float))
            {
                return VectorMath.IsOddIntegerSingle<Vector<float>, Vector<uint>>(vector.As<T, float>()).As<float, T>();
            }
            else if (typeof(T) == typeof(double))
            {
                return VectorMath.IsOddIntegerDouble<Vector<double>, Vector<ulong>>(vector.As<T, double>()).As<double, T>();
            }
            return ~IsZero(vector & Vector<T>.One);
        }

        /// <inheritdoc cref="Vector128.IsPositive{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> IsPositive<T>(Vector<T> vector)
        {
            if ((typeof(T) == typeof(byte))
             || (typeof(T) == typeof(ushort))
             || (typeof(T) == typeof(uint))
             || (typeof(T) == typeof(ulong))
             || (typeof(T) == typeof(nuint)))
            {
                return Vector<T>.AllBitsSet;
            }
            else if (typeof(T) == typeof(float))
            {
                return GreaterThanOrEqual(vector.As<T, int>(), Vector<int>.Zero).As<int, T>();
            }
            else if (typeof(T) == typeof(double))
            {
                return GreaterThanOrEqual(vector.As<T, long>(), Vector<long>.Zero).As<long, T>();
            }
            else
            {
                return GreaterThanOrEqual(vector, Vector<T>.Zero);
            }
        }

        /// <inheritdoc cref="Vector128.IsPositiveInfinity{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> IsPositiveInfinity<T>(Vector<T> vector)
        {
            if (typeof(T) == typeof(float))
            {
                return Equals(vector, Create(float.PositiveInfinity).As<float, T>());
            }
            else if (typeof(T) == typeof(double))
            {
                return Equals(vector, Create(double.PositiveInfinity).As<double, T>());
            }
            return Vector<T>.Zero;
        }

        /// <inheritdoc cref="Vector128.IsSubnormal{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> IsSubnormal<T>(Vector<T> vector)
        {
            if (typeof(T) == typeof(float))
            {
                return LessThan(Abs(vector).As<T, uint>() - Vector<uint>.One, Create<uint>(float.MaxTrailingSignificand)).As<uint, T>();
            }
            else if (typeof(T) == typeof(double))
            {
                return LessThan(Abs(vector).As<T, ulong>() - Vector<ulong>.One, Create<ulong>(double.MaxTrailingSignificand)).As<ulong, T>();
            }
            return Vector<T>.Zero;
        }

        /// <inheritdoc cref="Vector128.IsZero{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> IsZero<T>(Vector<T> vector) => Equals(vector, Vector<T>.Zero);

        /// <inheritdoc cref="Vector128.LastIndexOf{T}(Vector128{T}, T)" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LastIndexOf<T>(Vector<T> vector, T value)
        {
            if (Vector<T>.Count == Vector512<T>.Count)
            {
                return Vector512.LastIndexOf(vector.AsVector512(), value);
            }
            else if (Vector<T>.Count == Vector256<T>.Count)
            {
                return Vector256.LastIndexOf(vector.AsVector256(), value);
            }
            else
            {
                return Vector128.LastIndexOf(vector.AsVector128(), value);
            }
        }

        /// <inheritdoc cref="Vector128.LastIndexOfWhereAllBitsSet{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LastIndexOfWhereAllBitsSet<T>(Vector<T> vector)
        {
            if (typeof(T) == typeof(float))
            {
                return LastIndexOf(vector.As<T, int>(), -1);
            }
            else if (typeof(T) == typeof(double))
            {
                return LastIndexOf(vector.As<T, long>(), -1);
            }
            else
            {
                return LastIndexOf(vector, Scalar<T>.AllBitsSet);
            }
        }

        internal static Vector<T> Lerp<T>(Vector<T> x, Vector<T> y, Vector<T> amount)
            where T : IFloatingPointIeee754<T>
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = T.Lerp(x.GetElementUnsafe(index), y.GetElementUnsafe(index), amount.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Performs a linear interpolation between two vectors based on the given weighting.</summary>
        /// <param name="x">The first vector.</param>
        /// <param name="y">The second vector.</param>
        /// <param name="amount">A value between 0 and 1 that indicates the weight of <paramref name="y" />.</param>
        /// <returns>The interpolated vector.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> Lerp(Vector<double> x, Vector<double> y, Vector<double> amount)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.Lerp<Vector<double>, double>(x, y, amount);
            }
            else
            {
                return Lerp<double>(x, y, amount);
            }
        }

        /// <summary>Performs a linear interpolation between two vectors based on the given weighting.</summary>
        /// <param name="x">The first vector.</param>
        /// <param name="y">The second vector.</param>
        /// <param name="amount">A value between 0 and 1 that indicates the weight of <paramref name="y" />.</param>
        /// <returns>The interpolated vector.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> Lerp(Vector<float> x, Vector<float> y, Vector<float> amount)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.Lerp<Vector<float>, float>(x, y, amount);
            }
            else
            {
                return Lerp<float>(x, y, amount);
            }
        }

        /// <summary>Compares two vectors to determine which is less on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were less.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> LessThan<T>(Vector<T> left, Vector<T> right)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = Scalar<T>.LessThan(left.GetElementUnsafe(index), right.GetElementUnsafe(index)) ? Scalar<T>.AllBitsSet : default!;
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Compares two vectors to determine which is less on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were less.</returns>
        [Intrinsic]
        public static Vector<long> LessThan(Vector<double> left, Vector<double> right) => LessThan<double>(left, right).As<double, long>();

        /// <summary>Compares two vectors to determine which is less on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were less.</returns>
        [Intrinsic]
        public static Vector<int> LessThan(Vector<int> left, Vector<int> right) => LessThan<int>(left, right);

        /// <summary>Compares two vectors to determine which is less on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were less.</returns>
        [Intrinsic]
        public static Vector<long> LessThan(Vector<long> left, Vector<long> right) => LessThan<long>(left, right);

        /// <summary>Compares two vectors to determine which is less on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were less.</returns>
        [Intrinsic]
        public static Vector<int> LessThan(Vector<float> left, Vector<float> right) => LessThan<float>(left, right).As<float, int>();

        /// <summary>Compares two vectors to determine if all elements are less.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns><c>true</c> if all elements in <paramref name="left" /> were less than the corresponding element in <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool LessThanAll<T>(Vector<T> left, Vector<T> right)
        {
            for (int index = 0; index < Vector<T>.Count; index++)
            {
                if (!Scalar<T>.LessThan(left.GetElementUnsafe(index), right.GetElementUnsafe(index)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Compares two vectors to determine if any elements are less.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns><c>true</c> if any elements in <paramref name="left" /> was less than the corresponding element in <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool LessThanAny<T>(Vector<T> left, Vector<T> right)
        {
            for (int index = 0; index < Vector<T>.Count; index++)
            {
                if (Scalar<T>.LessThan(left.GetElementUnsafe(index), right.GetElementUnsafe(index)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Compares two vectors to determine which is less or equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were less or equal.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> LessThanOrEqual<T>(Vector<T> left, Vector<T> right)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = Scalar<T>.LessThanOrEqual(left.GetElementUnsafe(index), right.GetElementUnsafe(index)) ? Scalar<T>.AllBitsSet : default!;
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Compares two vectors to determine which is less or equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were less or equal.</returns>
        [Intrinsic]
        public static Vector<long> LessThanOrEqual(Vector<double> left, Vector<double> right) => LessThanOrEqual<double>(left, right).As<double, long>();

        /// <summary>Compares two vectors to determine which is less or equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were less or equal.</returns>
        [Intrinsic]
        public static Vector<int> LessThanOrEqual(Vector<int> left, Vector<int> right) => LessThanOrEqual<int>(left, right);

        /// <summary>Compares two vectors to determine which is less or equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were less or equal.</returns>
        [Intrinsic]
        public static Vector<long> LessThanOrEqual(Vector<long> left, Vector<long> right) => LessThanOrEqual<long>(left, right);

        /// <summary>Compares two vectors to determine which is less or equal on a per-element basis.</summary>
        /// <param name="left">The vector to compare with <paramref name="left" />.</param>
        /// <param name="right">The vector to compare with <paramref name="right" />.</param>
        /// <returns>A vector whose elements are all-bits-set or zero, depending on if which of the corresponding elements in <paramref name="left" /> and <paramref name="right" /> were less or equal.</returns>
        [Intrinsic]
        public static Vector<int> LessThanOrEqual(Vector<float> left, Vector<float> right) => LessThanOrEqual<float>(left, right).As<float, int>();

        /// <summary>Compares two vectors to determine if all elements are less or equal.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns><c>true</c> if all elements in <paramref name="left" /> were less than or equal to the corresponding element in <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool LessThanOrEqualAll<T>(Vector<T> left, Vector<T> right)
        {
            for (int index = 0; index < Vector<T>.Count; index++)
            {
                if (!Scalar<T>.LessThanOrEqual(left.GetElementUnsafe(index), right.GetElementUnsafe(index)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Compares two vectors to determine if any elements are less or equal.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns><c>true</c> if any elements in <paramref name="left" /> was less than or equal to the corresponding element in <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool LessThanOrEqualAny<T>(Vector<T> left, Vector<T> right)
        {
            for (int index = 0; index < Vector<T>.Count; index++)
            {
                if (Scalar<T>.LessThanOrEqual(left.GetElementUnsafe(index), right.GetElementUnsafe(index)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Loads a vector from the given source.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="source">The source from which the vector will be loaded.</param>
        /// <returns>The vector loaded from <paramref name="source" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="source" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static unsafe Vector<T> Load<T>(T* source) => LoadUnsafe(ref *source);

        /// <summary>Loads a vector from the given aligned source.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="source">The aligned source from which the vector will be loaded.</param>
        /// <returns>The vector loaded from <paramref name="source" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="source" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Vector<T> LoadAligned<T>(T* source)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();

            if (((nuint)(source) % (uint)(Alignment)) != 0)
            {
                ThrowHelper.ThrowAccessViolationException();
            }

            return *(Vector<T>*)source;
        }

        /// <summary>Loads a vector from the given aligned source.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="source">The aligned source from which the vector will be loaded.</param>
        /// <returns>The vector loaded from <paramref name="source" />.</returns>
        /// <remarks>This method may bypass the cache on certain platforms.</remarks>
        /// <exception cref="NotSupportedException">The type of <paramref name="source" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static unsafe Vector<T> LoadAlignedNonTemporal<T>(T* source) => LoadAligned(source);

        /// <summary>Loads a vector from the given source.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="source">The source from which the vector will be loaded.</param>
        /// <returns>The vector loaded from <paramref name="source" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="source" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> LoadUnsafe<T>(ref readonly T source)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
            ref readonly byte address = ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in source));
            return Unsafe.ReadUnaligned<Vector<T>>(in address);
        }

        /// <summary>Loads a vector from the given source and element offset.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="source">The source to which <paramref name="elementOffset" /> will be added before loading the vector.</param>
        /// <param name="elementOffset">The element offset from <paramref name="source" /> from which the vector will be loaded.</param>
        /// <returns>The vector loaded from <paramref name="source" /> plus <paramref name="elementOffset" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="source" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> LoadUnsafe<T>(ref readonly T source, nuint elementOffset)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
            ref readonly byte address = ref Unsafe.As<T, byte>(ref Unsafe.Add(ref Unsafe.AsRef(in source), (nint)elementOffset));
            return Unsafe.ReadUnaligned<Vector<T>>(in address);
        }

        internal static Vector<T> Log<T>(Vector<T> vector)
            where T : ILogarithmicFunctions<T>
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = T.Log(vector.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <inheritdoc cref="Vector128.Log(Vector128{double})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> Log(Vector<double> vector)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.LogDouble<Vector<double>, Vector<long>, Vector<ulong>>(vector);
            }
            else
            {
                return Log<double>(vector);
            }
        }

        /// <inheritdoc cref="Vector128.Log(Vector128{float})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> Log(Vector<float> vector)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.LogSingle<Vector<float>, Vector<int>, Vector<uint>>(vector);
            }
            else
            {
                return Log<float>(vector);
            }
        }

        internal static Vector<T> Log2<T>(Vector<T> vector)
            where T : ILogarithmicFunctions<T>
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = T.Log2(vector.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <inheritdoc cref="Vector128.Log2(Vector128{double})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> Log2(Vector<double> vector)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.Log2Double<Vector<double>, Vector<long>, Vector<ulong>>(vector);
            }
            else
            {
                return Log2<double>(vector);
            }
        }

        /// <inheritdoc cref="Vector128.Log2(Vector128{float})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> Log2(Vector<float> vector)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.Log2Single<Vector<float>, Vector<int>, Vector<uint>>(vector);
            }
            else
            {
                return Log2<float>(vector);
            }
        }

        /// <inheritdoc cref="Vector128.Max{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> Max<T>(Vector<T> left, Vector<T> right)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.Max<Vector<T>, T>(left, right);
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.Max(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.MaxMagnitude{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> MaxMagnitude<T>(Vector<T> left, Vector<T> right)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.MaxMagnitude<Vector<T>, T>(left, right);
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.MaxMagnitude(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.MaxMagnitudeNumber{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> MaxMagnitudeNumber<T>(Vector<T> left, Vector<T> right)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.MaxMagnitudeNumber<Vector<T>, T>(left, right);
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.MaxMagnitudeNumber(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.MaxNative{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> MaxNative<T>(Vector<T> left, Vector<T> right)
        {
            if (IsHardwareAccelerated)
            {
                return ConditionalSelect(GreaterThan(left, right), left, right);
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.GreaterThan(left.GetElementUnsafe(index), right.GetElementUnsafe(index)) ? left.GetElementUnsafe(index) : right.GetElementUnsafe(index);
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.MaxNumber{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> MaxNumber<T>(Vector<T> left, Vector<T> right)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.MaxNumber<Vector<T>, T>(left, right);
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.MaxNumber(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.Min{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> Min<T>(Vector<T> left, Vector<T> right)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.Min<Vector<T>, T>(left, right);
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.Min(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.MinMagnitude{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> MinMagnitude<T>(Vector<T> left, Vector<T> right)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.MinMagnitude<Vector<T>, T>(left, right);
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.MinMagnitude(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.MinMagnitudeNumber{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> MinMagnitudeNumber<T>(Vector<T> left, Vector<T> right)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.MinMagnitudeNumber<Vector<T>, T>(left, right);
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.MinMagnitudeNumber(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.MinNative{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> MinNative<T>(Vector<T> left, Vector<T> right)
        {
            if (IsHardwareAccelerated)
            {
                return ConditionalSelect(LessThan(left, right), left, right);
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.LessThan(left.GetElementUnsafe(index), right.GetElementUnsafe(index)) ? left.GetElementUnsafe(index) : right.GetElementUnsafe(index);
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.MinNumber{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> MinNumber<T>(Vector<T> left, Vector<T> right)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.MinNumber<Vector<T>, T>(left, right);
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.MinNumber(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <summary>Multiplies two vectors to compute their element-wise product.</summary>
        /// <param name="left">The vector to multiply with <paramref name="right" />.</param>
        /// <param name="right">The vector to multiply with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>The element-wise product of <paramref name="left" /> and <paramref name="right" />.</returns>
        [Intrinsic]
        public static Vector<T> Multiply<T>(Vector<T> left, Vector<T> right) => left * right;

        /// <summary>Multiplies a vector by a scalar to compute their product.</summary>
        /// <param name="left">The vector to multiply with <paramref name="right" />.</param>
        /// <param name="right">The scalar to multiply with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>The product of <paramref name="left" /> and <paramref name="right" />.</returns>
        [Intrinsic]
        public static Vector<T> Multiply<T>(Vector<T> left, T right) => left * right;

        /// <summary>Multiplies a vector by a scalar to compute their product.</summary>
        /// <param name="left">The scalar to multiply with <paramref name="right" />.</param>
        /// <param name="right">The vector to multiply with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>The product of <paramref name="left" /> and <paramref name="right" />.</returns>
        [Intrinsic]
        public static Vector<T> Multiply<T>(T left, Vector<T> right) => right * left;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector<T> MultiplyAddEstimate<T>(Vector<T> left, Vector<T> right, Vector<T> addend)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = Scalar<T>.MultiplyAddEstimate(left.GetElementUnsafe(index), right.GetElementUnsafe(index), addend.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <inheritdoc cref="Vector128.MultiplyAddEstimate(Vector128{double}, Vector128{double}, Vector128{double})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> MultiplyAddEstimate(Vector<double> left, Vector<double> right, Vector<double> addend)
        {
            Unsafe.SkipInit(out Vector<double> result);

            for (int index = 0; index < Vector<double>.Count; index++)
            {
                double element = double.MultiplyAddEstimate(left.GetElementUnsafe(index), right.GetElementUnsafe(index), addend.GetElementUnsafe(index));
                result.SetElementUnsafe(index, element);
            }

            return result;
        }

        /// <inheritdoc cref="Vector128.MultiplyAddEstimate(Vector128{float}, Vector128{float}, Vector128{float})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> MultiplyAddEstimate(Vector<float> left, Vector<float> right, Vector<float> addend)
        {
            Unsafe.SkipInit(out Vector<float> result);

            for (int index = 0; index < Vector<float>.Count; index++)
            {
                float element = float.MultiplyAddEstimate(left.GetElementUnsafe(index), right.GetElementUnsafe(index), addend.GetElementUnsafe(index));
                result.SetElementUnsafe(index, element);
            }

            return result;
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector<TResult> Narrow<TSource, TResult>(Vector<TSource> low, Vector<TSource> high)
            where TSource : INumber<TSource>
            where TResult : INumber<TResult>
        {
            Unsafe.SkipInit(out Vector<TResult> result);

            for (int i = 0; i < Vector<TSource>.Count; i++)
            {
                TResult value = TResult.CreateTruncating(low.GetElementUnsafe(i));
                result.SetElementUnsafe(i, value);
            }

            for (int i = Vector<TSource>.Count; i < Vector<TResult>.Count; i++)
            {
                TResult value = TResult.CreateTruncating(high.GetElementUnsafe(i - Vector<TSource>.Count));
                result.SetElementUnsafe(i, value);
            }

            return result;
        }

        /// <inheritdoc cref="Vector128.Narrow(Vector128{double}, Vector128{double})"/>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> Narrow(Vector<double> low, Vector<double> high)
            => Narrow<double, float>(low, high);

        /// <inheritdoc cref="Vector128.Narrow(Vector128{short}, Vector128{short})"/>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<sbyte> Narrow(Vector<short> low, Vector<short> high)
            => Narrow<short, sbyte>(low, high);

        /// <inheritdoc cref="Vector128.Narrow(Vector128{int}, Vector128{int})"/>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<short> Narrow(Vector<int> low, Vector<int> high)
            => Narrow<int, short>(low, high);

        /// <inheritdoc cref="Vector128.Narrow(Vector128{long}, Vector128{long})"/>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<int> Narrow(Vector<long> low, Vector<long> high)
            => Narrow<long, int>(low, high);

        /// <inheritdoc cref="Vector128.Narrow(Vector128{ushort}, Vector128{ushort})"/>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<byte> Narrow(Vector<ushort> low, Vector<ushort> high)
            => Narrow<ushort, byte>(low, high);

        /// <inheritdoc cref="Vector128.Narrow(Vector128{uint}, Vector128{uint})"/>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<ushort> Narrow(Vector<uint> low, Vector<uint> high)
            => Narrow<uint, ushort>(low, high);

        /// <inheritdoc cref="Vector128.Narrow(Vector128{ulong}, Vector128{ulong})"/>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<uint> Narrow(Vector<ulong> low, Vector<ulong> high)
            => Narrow<ulong, uint>(low, high);

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector<TResult> NarrowWithSaturation<TSource, TResult>(Vector<TSource> low, Vector<TSource> high)
            where TSource : INumber<TSource>
            where TResult : INumber<TResult>
        {
            Unsafe.SkipInit(out Vector<TResult> result);

            for (int i = 0; i < Vector<TSource>.Count; i++)
            {
                TResult value = TResult.CreateSaturating(low.GetElementUnsafe(i));
                result.SetElementUnsafe(i, value);
            }

            for (int i = Vector<TSource>.Count; i < Vector<TResult>.Count; i++)
            {
                TResult value = TResult.CreateSaturating(high.GetElementUnsafe(i - Vector<TSource>.Count));
                result.SetElementUnsafe(i, value);
            }

            return result;
        }

        /// <inheritdoc cref="Vector128.NarrowWithSaturation(Vector128{double}, Vector128{double})"/>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> NarrowWithSaturation(Vector<double> low, Vector<double> high)
            => NarrowWithSaturation<double, float>(low, high);

        /// <inheritdoc cref="Vector128.NarrowWithSaturation(Vector128{short}, Vector128{short})"/>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<sbyte> NarrowWithSaturation(Vector<short> low, Vector<short> high)
            => NarrowWithSaturation<short, sbyte>(low, high);

        /// <inheritdoc cref="Vector128.NarrowWithSaturation(Vector128{int}, Vector128{int})"/>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<short> NarrowWithSaturation(Vector<int> low, Vector<int> high)
            => NarrowWithSaturation<int, short>(low, high);

        /// <inheritdoc cref="Vector128.NarrowWithSaturation(Vector128{long}, Vector128{long})"/>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<int> NarrowWithSaturation(Vector<long> low, Vector<long> high)
            => NarrowWithSaturation<long, int>(low, high);

        /// <inheritdoc cref="Vector128.NarrowWithSaturation(Vector128{ushort}, Vector128{ushort})"/>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<byte> NarrowWithSaturation(Vector<ushort> low, Vector<ushort> high)
            => NarrowWithSaturation<ushort, byte>(low, high);

        /// <inheritdoc cref="Vector128.NarrowWithSaturation(Vector128{uint}, Vector128{uint})"/>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<ushort> NarrowWithSaturation(Vector<uint> low, Vector<uint> high)
            => NarrowWithSaturation<uint, ushort>(low, high);

        /// <inheritdoc cref="Vector128.NarrowWithSaturation(Vector128{ulong}, Vector128{ulong})"/>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<uint> NarrowWithSaturation(Vector<ulong> low, Vector<ulong> high)
            => NarrowWithSaturation<ulong, uint>(low, high);

        /// <summary>Computes the unary negation of a vector.</summary>
        /// <param name="value">The vector to negate.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>A vector whose elements are the unary negation of the corresponding elements in <paramref name="value" />.</returns>
        [Intrinsic]
        public static Vector<T> Negate<T>(Vector<T> value) => -value;

        /// <inheritdoc cref="Vector128.None{T}(Vector128{T}, T)" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool None<T>(Vector<T> vector, T value) => !EqualsAny(vector, Create(value));

        /// <inheritdoc cref="Vector128.NoneWhereAllBitsSet{T}(Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool NoneWhereAllBitsSet<T>(Vector<T> vector)
        {
            if (typeof(T) == typeof(float))
            {
                return None(vector.As<T, int>(), -1);
            }
            else if (typeof(T) == typeof(double))
            {
                return None(vector.As<T, long>(), -1);
            }
            else
            {
                return None(vector, Scalar<T>.AllBitsSet);
            }
        }

        /// <summary>Computes the ones-complement of a vector.</summary>
        /// <param name="value">The vector whose ones-complement is to be computed.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>A vector whose elements are the ones-complement of the corresponding elements in <paramref name="value" />.</returns>
        [Intrinsic]
        public static Vector<T> OnesComplement<T>(Vector<T> value) => ~value;

        internal static Vector<T> RadiansToDegrees<T>(Vector<T> radians)
            where T : ITrigonometricFunctions<T>
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = T.RadiansToDegrees(radians.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <inheritdoc cref="Vector128.RadiansToDegrees(Vector128{double})" />
        [Intrinsic]
        public static Vector<double> RadiansToDegrees(Vector<double> radians)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.RadiansToDegrees<Vector<double>, double>(radians);
            }
            else
            {
                return RadiansToDegrees<double>(radians);
            }
        }

        /// <inheritdoc cref="Vector128.RadiansToDegrees(Vector128{float})" />
        [Intrinsic]
        public static Vector<float> RadiansToDegrees(Vector<float> radians)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.RadiansToDegrees<Vector<float>, float>(radians);
            }
            else
            {
                return RadiansToDegrees<float>(radians);
            }
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector<T> Round<T>(Vector<T> vector)
        {
            if ((typeof(T) == typeof(byte))
             || (typeof(T) == typeof(short))
             || (typeof(T) == typeof(int))
             || (typeof(T) == typeof(long))
             || (typeof(T) == typeof(nint))
             || (typeof(T) == typeof(nuint))
             || (typeof(T) == typeof(sbyte))
             || (typeof(T) == typeof(ushort))
             || (typeof(T) == typeof(uint))
             || (typeof(T) == typeof(ulong)))
            {
                return vector;
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.Round(vector.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.Round(Vector128{double})" />
        [Intrinsic]
        public static Vector<double> Round(Vector<double> vector) => Round<double>(vector);

        /// <inheritdoc cref="Vector128.Round(Vector128{float})" />
        [Intrinsic]
        public static Vector<float> Round(Vector<float> vector) => Round<float>(vector);

        /// <inheritdoc cref="Vector128.Round(Vector128{double}, MidpointRounding)" />
        [Intrinsic]
        public static Vector<double> Round(Vector<double> vector, MidpointRounding mode) => VectorMath.RoundDouble(vector, mode);

        /// <inheritdoc cref="Vector128.Round(Vector128{float}, MidpointRounding)" />
        [Intrinsic]
        public static Vector<float> Round(Vector<float> vector, MidpointRounding mode) => VectorMath.RoundSingle(vector, mode);

        /// <summary>Shifts each element of a vector left by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted left by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<byte> ShiftLeft(Vector<byte> value, int shiftCount) => value << shiftCount;

        /// <summary>Shifts each element of a vector left by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted left by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<short> ShiftLeft(Vector<short> value, int shiftCount) => value << shiftCount;

        /// <summary>Shifts each element of a vector left by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted left by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<int> ShiftLeft(Vector<int> value, int shiftCount) => value << shiftCount;

        /// <summary>Shifts each element of a vector left by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted left by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<long> ShiftLeft(Vector<long> value, int shiftCount) => value << shiftCount;

        /// <summary>Shifts each element of a vector left by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted left by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<nint> ShiftLeft(Vector<nint> value, int shiftCount) => value << shiftCount;

        /// <summary>Shifts each element of a vector left by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted left by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<nuint> ShiftLeft(Vector<nuint> value, int shiftCount) => value << shiftCount;

        /// <summary>Shifts each element of a vector left by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted left by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<sbyte> ShiftLeft(Vector<sbyte> value, int shiftCount) => value << shiftCount;

        /// <summary>Shifts each element of a vector left by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted left by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<ushort> ShiftLeft(Vector<ushort> value, int shiftCount) => value << shiftCount;

        /// <summary>Shifts each element of a vector left by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted left by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<uint> ShiftLeft(Vector<uint> value, int shiftCount) => value << shiftCount;

        [Intrinsic]
        internal static Vector<uint> ShiftLeft(Vector<uint> vector, Vector<uint> shiftCount)
        {
            if (Vector<uint>.Count == Vector512<uint>.Count)
            {
                return Vector512.ShiftLeft(vector.AsVector512(), shiftCount.AsVector512()).AsVector();
            }
            else if (Vector<uint>.Count == Vector256<uint>.Count)
            {
                return Vector256.ShiftLeft(vector.AsVector256(), shiftCount.AsVector256()).AsVector();
            }
            else
            {
                return Vector128.ShiftLeft(vector.AsVector128(), shiftCount.AsVector128()).AsVector();
            }
        }

        /// <summary>Shifts each element of a vector left by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted left by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<ulong> ShiftLeft(Vector<ulong> value, int shiftCount) => value << shiftCount;

        [Intrinsic]
        internal static Vector<ulong> ShiftLeft(Vector<ulong> vector, Vector<ulong> shiftCount)
        {
            if (Vector<ulong>.Count == Vector512<ulong>.Count)
            {
                return Vector512.ShiftLeft(vector.AsVector512(), shiftCount.AsVector512()).AsVector();
            }
            else if (Vector<ulong>.Count == Vector256<ulong>.Count)
            {
                return Vector256.ShiftLeft(vector.AsVector256(), shiftCount.AsVector256()).AsVector();
            }
            else
            {
                return Vector128.ShiftLeft(vector.AsVector128(), shiftCount.AsVector128()).AsVector();
            }
        }

        /// <summary>Shifts (signed) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<short> ShiftRightArithmetic(Vector<short> value, int shiftCount) => value >> shiftCount;

        /// <summary>Shifts (signed) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<int> ShiftRightArithmetic(Vector<int> value, int shiftCount) => value >> shiftCount;

        /// <summary>Shifts (signed) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<long> ShiftRightArithmetic(Vector<long> value, int shiftCount) => value >> shiftCount;

        /// <summary>Shifts (signed) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<nint> ShiftRightArithmetic(Vector<nint> value, int shiftCount) => value >> shiftCount;

        /// <summary>Shifts (signed) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<sbyte> ShiftRightArithmetic(Vector<sbyte> value, int shiftCount) => value >> shiftCount;

        /// <summary>Shifts (unsigned) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<byte> ShiftRightLogical(Vector<byte> value, int shiftCount) => value >>> shiftCount;

        /// <summary>Shifts (unsigned) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<short> ShiftRightLogical(Vector<short> value, int shiftCount) => value >>> shiftCount;

        /// <summary>Shifts (unsigned) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<int> ShiftRightLogical(Vector<int> value, int shiftCount) => value >>> shiftCount;

        /// <summary>Shifts (unsigned) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<long> ShiftRightLogical(Vector<long> value, int shiftCount) => value >>> shiftCount;

        /// <summary>Shifts (unsigned) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        public static Vector<nint> ShiftRightLogical(Vector<nint> value, int shiftCount) => value >>> shiftCount;

        /// <summary>Shifts (unsigned) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<nuint> ShiftRightLogical(Vector<nuint> value, int shiftCount) => value >>> shiftCount;

        /// <summary>Shifts (unsigned) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<sbyte> ShiftRightLogical(Vector<sbyte> value, int shiftCount) => value >>> shiftCount;

        /// <summary>Shifts (unsigned) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<ushort> ShiftRightLogical(Vector<ushort> value, int shiftCount) => value >>> shiftCount;

        /// <summary>Shifts (unsigned) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<uint> ShiftRightLogical(Vector<uint> value, int shiftCount) => value >>> shiftCount;

        /// <summary>Shifts (unsigned) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        public static Vector<ulong> ShiftRightLogical(Vector<ulong> value, int shiftCount) => value >>> shiftCount;

        internal static Vector<T> Sin<T>(Vector<T> vector)
            where T : ITrigonometricFunctions<T>
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T value = T.Sin(vector.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <inheritdoc cref="Vector128.Sin(Vector128{double})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> Sin(Vector<double> vector)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.SinDouble<Vector<double>, Vector<long>>(vector);
            }
            else
            {
                return Sin<double>(vector);
            }
        }

        /// <inheritdoc cref="Vector128.Sin(Vector128{float})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> Sin(Vector<float> vector)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.SinSingle<Vector<float>, Vector<int>, Vector<double>, Vector<long>>(vector);
            }
            else
            {
                return Sin<float>(vector);
            }
        }

        internal static (Vector<T> Sin, Vector<T> Cos) SinCos<T>(Vector<T> vector)
            where T : ITrigonometricFunctions<T>
        {
            Unsafe.SkipInit(out Vector<T> sinResult);
            Unsafe.SkipInit(out Vector<T> cosResult);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                (T sinValue, T cosValue) = T.SinCos(vector.GetElementUnsafe(index));
                sinResult.SetElementUnsafe(index, sinValue);
                cosResult.SetElementUnsafe(index, cosValue);
            }

            return (sinResult, cosResult);
        }

        /// <inheritdoc cref="Vector128.SinCos(Vector128{double})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (Vector<double> Sin, Vector<double> Cos) SinCos(Vector<double> vector)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.SinCosDouble<Vector<double>, Vector<long>>(vector);
            }
            else
            {
                return SinCos<double>(vector);
            }
        }

        /// <inheritdoc cref="Vector128.SinCos(Vector128{float})" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (Vector<float> Sin, Vector<float> Cos) SinCos(Vector<float> vector)
        {
            if (IsHardwareAccelerated)
            {
                return VectorMath.SinCosSingle<Vector<float>, Vector<int>, Vector<double>, Vector<long>>(vector);
            }
            else
            {
                return SinCos<float>(vector);
            }
        }

        /// <summary>Computes the square root of a vector on a per-element basis.</summary>
        /// <param name="value">The vector whose square root is to be computed.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>A vector whose elements are the square root of the corresponding elements in <paramref name="value" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> SquareRoot<T>(Vector<T> value)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                T element = Scalar<T>.Sqrt(value.GetElementUnsafe(index));
                result.SetElementUnsafe(index, element);
            }

            return result;
        }

        /// <summary>Stores a vector at the given destination.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="source">The vector that will be stored.</param>
        /// <param name="destination">The destination at which <paramref name="source" /> will be stored.</param>
        /// <exception cref="NotSupportedException">The type of <paramref name="source" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static unsafe void Store<T>(this Vector<T> source, T* destination) => source.StoreUnsafe(ref *destination);

        /// <summary>Stores a vector at the given aligned destination.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="source">The vector that will be stored.</param>
        /// <param name="destination">The aligned destination at which <paramref name="source" /> will be stored.</param>
        /// <exception cref="NotSupportedException">The type of <paramref name="source" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void StoreAligned<T>(this Vector<T> source, T* destination)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();

            if (((nuint)destination % (uint)(Alignment)) != 0)
            {
                ThrowHelper.ThrowAccessViolationException();
            }

            *(Vector<T>*)destination = source;
        }

        /// <summary>Stores a vector at the given aligned destination.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="source">The vector that will be stored.</param>
        /// <param name="destination">The aligned destination at which <paramref name="source" /> will be stored.</param>
        /// <remarks>This method may bypass the cache on certain platforms.</remarks>
        /// <exception cref="NotSupportedException">The type of <paramref name="source" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static unsafe void StoreAlignedNonTemporal<T>(this Vector<T> source, T* destination) => source.StoreAligned(destination);

        /// <summary>Stores a vector at the given destination.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="source">The vector that will be stored.</param>
        /// <param name="destination">The destination at which <paramref name="source" /> will be stored.</param>
        /// <exception cref="NotSupportedException">The type of <paramref name="source" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreUnsafe<T>(this Vector<T> source, ref T destination)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
            ref byte address = ref Unsafe.As<T, byte>(ref destination);
            Unsafe.WriteUnaligned(ref address, source);
        }

        /// <summary>Stores a vector at the given destination.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="source">The vector that will be stored.</param>
        /// <param name="destination">The destination to which <paramref name="elementOffset" /> will be added before the vector will be stored.</param>
        /// <param name="elementOffset">The element offset from <paramref name="destination" /> from which the vector will be stored.</param>
        /// <exception cref="NotSupportedException">The type of <paramref name="source" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreUnsafe<T>(this Vector<T> source, ref T destination, nuint elementOffset)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
            destination = ref Unsafe.Add(ref destination, (nint)elementOffset);
            Unsafe.WriteUnaligned(ref Unsafe.As<T, byte>(ref destination), source);
        }

        /// <inheritdoc cref="Vector128.Subtract{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        public static Vector<T> Subtract<T>(Vector<T> left, Vector<T> right) => left - right;

        /// <inheritdoc cref="Vector128.SubtractSaturate{T}(Vector128{T}, Vector128{T})" />
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> SubtractSaturate<T>(Vector<T> left, Vector<T> right)
        {
            if ((typeof(T) == typeof(float)) || (typeof(T) == typeof(double)))
            {
                return left - right;
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.SubtractSaturate(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <summary>
        /// Returns the sum of all elements inside the vector.
        /// </summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Sum<T>(Vector<T> value)
        {
            T sum = default!;

            for (int index = 0; index < Vector<T>.Count; index++)
            {
                sum = Scalar<T>.Add(sum, value.GetElementUnsafe(index));
            }

            return sum;
        }

        /// <summary>Converts the given vector to a scalar containing the value of the first element.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="vector">The vector to get the first element from.</param>
        /// <returns>A scalar <typeparamref name="T" /> containing the value of the first element.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="vector" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static T ToScalar<T>(this Vector<T> vector)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
            return vector.GetElementUnsafe(0);
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector<T> Truncate<T>(Vector<T> vector)
        {
            if ((typeof(T) == typeof(byte))
             || (typeof(T) == typeof(short))
             || (typeof(T) == typeof(int))
             || (typeof(T) == typeof(long))
             || (typeof(T) == typeof(nint))
             || (typeof(T) == typeof(nuint))
             || (typeof(T) == typeof(sbyte))
             || (typeof(T) == typeof(ushort))
             || (typeof(T) == typeof(uint))
             || (typeof(T) == typeof(ulong)))
            {
                return vector;
            }
            else
            {
                Unsafe.SkipInit(out Vector<T> result);

                for (int index = 0; index < Vector<T>.Count; index++)
                {
                    T value = Scalar<T>.Truncate(vector.GetElementUnsafe(index));
                    result.SetElementUnsafe(index, value);
                }

                return result;
            }
        }

        /// <inheritdoc cref="Vector128.Truncate(Vector128{double})" />
        [Intrinsic]
        public static Vector<double> Truncate(Vector<double> vector) => Truncate<double>(vector);

        /// <inheritdoc cref="Vector128.Truncate(Vector128{float})" />
        [Intrinsic]
        public static Vector<float> Truncate(Vector<float> vector) => Truncate<float>(vector);

        /// <summary>Widens a <see langword="Vector&lt;Byte&gt;" /> into two <see cref="Vector{UInt16} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A pair of vectors that contain the widened lower and upper halves of <paramref name="source" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector<ushort> Lower, Vector<ushort> Upper) Widen(Vector<byte> source) => (WidenLower(source), WidenUpper(source));

        /// <summary>Widens a <see langword="Vector&lt;Int16&gt;" /> into two <see cref="Vector{Int32} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A pair of vectors that contain the widened lower and upper halves of <paramref name="source" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector<int> Lower, Vector<int> Upper) Widen(Vector<short> source) => (WidenLower(source), WidenUpper(source));

        /// <summary>Widens a <see langword="Vector&lt;Int32&gt;" /> into two <see cref="Vector{Int64} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A pair of vectors that contain the widened lower and upper halves of <paramref name="source" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector<long> Lower, Vector<long> Upper) Widen(Vector<int> source) => (WidenLower(source), WidenUpper(source));

        /// <summary>Widens a <see langword="Vector&lt;SByte&gt;" /> into two <see cref="Vector{Int16} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A pair of vectors that contain the widened lower and upper halves of <paramref name="source" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector<short> Lower, Vector<short> Upper) Widen(Vector<sbyte> source) => (WidenLower(source), WidenUpper(source));

        /// <summary>Widens a <see langword="Vector&lt;Single&gt;" /> into two <see cref="Vector{Double} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A pair of vectors that contain the widened lower and upper halves of <paramref name="source" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector<double> Lower, Vector<double> Upper) Widen(Vector<float> source) => (WidenLower(source), WidenUpper(source));

        /// <summary>Widens a <see langword="Vector&lt;UInt16&gt;" /> into two <see cref="Vector{UInt32} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A pair of vectors that contain the widened lower and upper halves of <paramref name="source" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector<uint> Lower, Vector<uint> Upper) Widen(Vector<ushort> source) => (WidenLower(source), WidenUpper(source));

        /// <summary>Widens a <see langword="Vector&lt;UInt32&gt;" /> into two <see cref="Vector{UInt64} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A pair of vectors that contain the widened lower and upper halves of <paramref name="source" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector<ulong> Lower, Vector<ulong> Upper) Widen(Vector<uint> source) => (WidenLower(source), WidenUpper(source));

        /// <summary>Widens a <see langword="Vector&lt;Byte&gt;" /> into two <see cref="Vector{UInt16} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <param name="low">A vector that will contain the widened result of the lower half of <paramref name="source" />.</param>
        /// <param name="high">A vector that will contain the widened result of the upper half of <paramref name="source" />.</param>
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Widen(Vector<byte> source, out Vector<ushort> low, out Vector<ushort> high)
        {
            low = WidenLower(source);
            high = WidenUpper(source);
        }

        /// <summary>Widens a <see langword="Vector&lt;Int16&gt;" /> into two <see cref="Vector{Int32} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <param name="low">A vector that will contain the widened result of the lower half of <paramref name="source" />.</param>
        /// <param name="high">A vector that will contain the widened result of the upper half of <paramref name="source" />.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Widen(Vector<short> source, out Vector<int> low, out Vector<int> high)
        {
            low = WidenLower(source);
            high = WidenUpper(source);
        }

        /// <summary>Widens a <see langword="Vector&lt;Int32&gt;" /> into two <see cref="Vector{Int64} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <param name="low">A vector that will contain the widened result of the lower half of <paramref name="source" />.</param>
        /// <param name="high">A vector that will contain the widened result of the upper half of <paramref name="source" />.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Widen(Vector<int> source, out Vector<long> low, out Vector<long> high)
        {
            low = WidenLower(source);
            high = WidenUpper(source);
        }

        /// <summary>Widens a <see langword="Vector&lt;SByte&gt;" /> into two <see cref="Vector{Int16} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <param name="low">A vector that will contain the widened result of the lower half of <paramref name="source" />.</param>
        /// <param name="high">A vector that will contain the widened result of the upper half of <paramref name="source" />.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static void Widen(Vector<sbyte> source, out Vector<short> low, out Vector<short> high)
        {
            low = WidenLower(source);
            high = WidenUpper(source);
        }

        /// <summary>Widens a <see langword="Vector&lt;Single&gt;" /> into two <see cref="Vector{Double} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <param name="low">A vector that will contain the widened result of the lower half of <paramref name="source" />.</param>
        /// <param name="high">A vector that will contain the widened result of the upper half of <paramref name="source" />.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Widen(Vector<float> source, out Vector<double> low, out Vector<double> high)
        {
            low = WidenLower(source);
            high = WidenUpper(source);
        }

        /// <summary>Widens a <see langword="Vector&lt;UInt16&gt;" /> into two <see cref="Vector{UInt32} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <param name="low">A vector that will contain the widened result of the lower half of <paramref name="source" />.</param>
        /// <param name="high">A vector that will contain the widened result of the upper half of <paramref name="source" />.</param>
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Widen(Vector<ushort> source, out Vector<uint> low, out Vector<uint> high)
        {
            low = WidenLower(source);
            high = WidenUpper(source);
        }

        /// <summary>Widens a <see langword="Vector&lt;UInt32&gt;" /> into two <see cref="Vector{UInt64} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <param name="low">A vector that will contain the widened result of the lower half of <paramref name="source" />.</param>
        /// <param name="high">A vector that will contain the widened result of the upper half of <paramref name="source" />.</param>
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Widen(Vector<uint> source, out Vector<ulong> low, out Vector<ulong> high)
        {
            low = WidenLower(source);
            high = WidenUpper(source);
        }

        /// <summary>Widens the lower half of a <see langword="Vector&lt;Byte&gt;" /> into a <see cref="Vector{UInt16} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened lower half of <paramref name="source" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<ushort> WidenLower(Vector<byte> source)
        {
            Unsafe.SkipInit(out Vector<ushort> lower);

            for (int i = 0; i < Vector<ushort>.Count; i++)
            {
                ushort value = source.GetElementUnsafe(i);
                lower.SetElementUnsafe(i, value);
            }

            return lower;
        }

        /// <summary>Widens the lower half of a <see langword="Vector&lt;Int16&gt;" /> into a <see cref="Vector{Int32} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened lower half of <paramref name="source" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<int> WidenLower(Vector<short> source)
        {
            Unsafe.SkipInit(out Vector<int> lower);

            for (int i = 0; i < Vector<int>.Count; i++)
            {
                int value = source.GetElementUnsafe(i);
                lower.SetElementUnsafe(i, value);
            }

            return lower;
        }

        /// <summary>Widens the lower half of a <see langword="Vector&lt;Int32&gt;" /> into a <see cref="Vector{Int64} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened lower half of <paramref name="source" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<long> WidenLower(Vector<int> source)
        {
            Unsafe.SkipInit(out Vector<long> lower);

            for (int i = 0; i < Vector<long>.Count; i++)
            {
                long value = source.GetElementUnsafe(i);
                lower.SetElementUnsafe(i, value);
            }

            return lower;
        }

        /// <summary>Widens the lower half of a <see langword="Vector&lt;SByte&gt;" /> into a <see cref="Vector{Int16} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened lower half of <paramref name="source" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<short> WidenLower(Vector<sbyte> source)
        {
            Unsafe.SkipInit(out Vector<short> lower);

            for (int i = 0; i < Vector<short>.Count; i++)
            {
                short value = source.GetElementUnsafe(i);
                lower.SetElementUnsafe(i, value);
            }

            return lower;
        }

        /// <summary>Widens the lower half of a <see langword="Vector&lt;Single&gt;" /> into a <see cref="Vector{Double} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened lower half of <paramref name="source" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> WidenLower(Vector<float> source)
        {
            Unsafe.SkipInit(out Vector<double> lower);

            for (int i = 0; i < Vector<double>.Count; i++)
            {
                double value = source.GetElementUnsafe(i);
                lower.SetElementUnsafe(i, value);
            }

            return lower;
        }

        /// <summary>Widens the lower half of a <see langword="Vector&lt;UInt16&gt;" /> into a <see cref="Vector{UInt32} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened lower half of <paramref name="source" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<uint> WidenLower(Vector<ushort> source)
        {
            Unsafe.SkipInit(out Vector<uint> lower);

            for (int i = 0; i < Vector<uint>.Count; i++)
            {
                uint value = source.GetElementUnsafe(i);
                lower.SetElementUnsafe(i, value);
            }

            return lower;
        }

        /// <summary>Widens the lower half of a <see langword="Vector&lt;UInt32&gt;" /> into a <see cref="Vector{UInt64} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened lower half of <paramref name="source" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<ulong> WidenLower(Vector<uint> source)
        {
            Unsafe.SkipInit(out Vector<ulong> lower);

            for (int i = 0; i < Vector<ulong>.Count; i++)
            {
                ulong value = source.GetElementUnsafe(i);
                lower.SetElementUnsafe(i, value);
            }

            return lower;
        }

        /// <summary>Widens the upper half of a <see langword="Vector&lt;Byte&gt;" /> into a <see cref="Vector{UInt16} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened upper half of <paramref name="source" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<ushort> WidenUpper(Vector<byte> source)
        {
            Unsafe.SkipInit(out Vector<ushort> upper);

            for (int i = Vector<ushort>.Count; i < Vector<byte>.Count; i++)
            {
                ushort value = source.GetElementUnsafe(i);
                upper.SetElementUnsafe(i - Vector<ushort>.Count, value);
            }

            return upper;
        }

        /// <summary>Widens the upper half of a <see langword="Vector&lt;Int16&gt;" /> into a <see cref="Vector{Int32} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened upper half of <paramref name="source" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<int> WidenUpper(Vector<short> source)
        {
            Unsafe.SkipInit(out Vector<int> upper);

            for (int i = Vector<int>.Count; i < Vector<short>.Count; i++)
            {
                int value = source.GetElementUnsafe(i);
                upper.SetElementUnsafe(i - Vector<int>.Count, value);
            }

            return upper;
        }

        /// <summary>Widens the upper half of a <see langword="Vector&lt;Int32&gt;" /> into a <see cref="Vector{Int64} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened upper half of <paramref name="source" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<long> WidenUpper(Vector<int> source)
        {
            Unsafe.SkipInit(out Vector<long> upper);

            for (int i = Vector<long>.Count; i < Vector<int>.Count; i++)
            {
                long value = source.GetElementUnsafe(i);
                upper.SetElementUnsafe(i - Vector<long>.Count, value);
            }

            return upper;
        }

        /// <summary>Widens the upper half of a <see langword="Vector&lt;SByte&gt;" /> into a <see cref="Vector{Int16} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened upper half of <paramref name="source" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<short> WidenUpper(Vector<sbyte> source)
        {
            Unsafe.SkipInit(out Vector<short> upper);

            for (int i = Vector<short>.Count; i < Vector<sbyte>.Count; i++)
            {
                short value = source.GetElementUnsafe(i);
                upper.SetElementUnsafe(i - Vector<short>.Count, value);
            }

            return upper;
        }

        /// <summary>Widens the upper half of a <see langword="Vector&lt;Single&gt;" /> into a <see cref="Vector{Double} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened upper half of <paramref name="source" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<double> WidenUpper(Vector<float> source)
        {
            Unsafe.SkipInit(out Vector<double> upper);

            for (int i = Vector<double>.Count; i < Vector<float>.Count; i++)
            {
                double value = source.GetElementUnsafe(i);
                upper.SetElementUnsafe(i - Vector<double>.Count, value);
            }

            return upper;
        }

        /// <summary>Widens the upper half of a <see langword="Vector&lt;UInt16&gt;" /> into a <see cref="Vector{UInt32} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened upper half of <paramref name="source" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<uint> WidenUpper(Vector<ushort> source)
        {
            Unsafe.SkipInit(out Vector<uint> upper);

            for (int i = Vector<uint>.Count; i < Vector<ushort>.Count; i++)
            {
                uint value = source.GetElementUnsafe(i);
                upper.SetElementUnsafe(i - Vector<uint>.Count, value);
            }

            return upper;
        }

        /// <summary>Widens the upper half of a <see langword="Vector&lt;UInt32&gt;" /> into a <see cref="Vector{UInt64} " />.</summary>
        /// <param name="source">The vector whose elements are to be widened.</param>
        /// <returns>A vector that contain the widened upper half of <paramref name="source" />.</returns>
        [Intrinsic]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<ulong> WidenUpper(Vector<uint> source)
        {
            Unsafe.SkipInit(out Vector<ulong> upper);

            for (int i = Vector<ulong>.Count; i < Vector<uint>.Count; i++)
            {
                ulong value = source.GetElementUnsafe(i);
                upper.SetElementUnsafe(i - Vector<ulong>.Count, value);
            }

            return upper;
        }

        /// <summary>Creates a new <see cref="Vector{T}" /> with the element at the specified index set to the specified value and the remaining elements set to the same value as that in the given vector.</summary>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <param name="vector">The vector to get the remaining elements from.</param>
        /// <param name="index">The index of the element to set.</param>
        /// <param name="value">The value to set the element to.</param>
        /// <returns>A <see cref="Vector{T}" /> with the value of the element at <paramref name="index" /> set to <paramref name="value" /> and the remaining elements set to the same value as that in <paramref name="vector" />.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> was less than zero or greater than the number of elements.</exception>
        /// <exception cref="NotSupportedException">The type of <paramref name="vector" /> (<typeparamref name="T" />) is not supported.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> WithElement<T>(this Vector<T> vector, int index, T value)
        {
            if ((uint)(index) >= (uint)(Vector<T>.Count))
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.index);
            }

            Vector<T> result = vector;
            result.SetElementUnsafe(index, value);
            return result;
        }

        /// <summary>Computes the exclusive-or of two vectors.</summary>
        /// <param name="left">The vector to exclusive-or with <paramref name="right" />.</param>
        /// <param name="right">The vector to exclusive-or with <paramref name="left" />.</param>
        /// <typeparam name="T">The type of the elements in the vector.</typeparam>
        /// <returns>The exclusive-or of <paramref name="left" /> and <paramref name="right" />.</returns>
        [Intrinsic]
        public static Vector<T> Xor<T>(Vector<T> left, Vector<T> right) => left ^ right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T GetElementUnsafe<T>(in this Vector<T> vector, int index)
        {
            ref T address = ref Unsafe.As<Vector<T>, T>(ref Unsafe.AsRef(in vector));
            return Unsafe.Add(ref address, index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SetElementUnsafe<T>(in this Vector<T> vector, int index, T value)
        {
            ref T address = ref Unsafe.As<Vector<T>, T>(ref Unsafe.AsRef(in vector));
            Unsafe.Add(ref address, index) = value;
        }
    }
    /* Note: The following patterns are used throughout the code here and are described here
    *
    * PATTERN:
    *    if (typeof(T) == typeof(int)) { ... }
    *    else if (typeof(T) == typeof(float)) { ... }
    * EXPLANATION:
    *    At runtime, each instantiation of Vector<T> will be type-specific, and each of these typeof blocks will be eliminated,
    *    as typeof(T) is a (JIT) compile-time constant for each instantiation. This design was chosen to eliminate any overhead from
    *    delegates and other patterns.
    *
    * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * */
    /// <summary>Represents a single vector of a specified numeric type that is suitable for low-level optimization of parallel algorithms.</summary>
    /// <typeparam name="T">The type of the elements in the vector. <typeparamref name="T" /> can be any primitive numeric type.</typeparam>
    [Intrinsic]
    public readonly unsafe struct Vector<T> : ISimdVector<Vector<T>, T>, IFormattable
    {
        // These fields exist to ensure the alignment is 8, rather than 1.
        internal readonly ulong _00;
        internal readonly ulong _01;

        /// <summary>Creates a new <see cref="Vector{T}" /> instance with all elements initialized to the specified value.</summary>
        /// <param name="value">The value that all elements will be initialized to.</param>
        /// <returns>A new <see cref="Vector{T}" /> with all elements initialized to <paramref name="value" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector(T value)
        {
            this = Vector.Create(value);
        }

        /// <summary>Creates a new <see cref="Vector{T}" /> from a given array.</summary>
        /// <param name="values">The array from which the vector is created.</param>
        /// <returns>A new <see cref="Vector{T}" /> with its elements set to the first <see cref="Vector{T}.Count" /> elements from <paramref name="values" />.</returns>
        /// <exception cref="NullReferenceException"><paramref name="values" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The length of <paramref name="values" /> is less than <see cref="Vector{T}.Count" />.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector(T[] values)
        {
            // We explicitly don't check for `null` because historically this has thrown `NullReferenceException` for perf reasons

            if (values.Length < Count)
            {
                ThrowHelper.ThrowArgumentOutOfRange_IndexMustBeLessOrEqualException();
            }

            this = Unsafe.ReadUnaligned<Vector<T>>(ref Unsafe.As<T, byte>(ref values[0]));
        }

        /// <summary>Creates a new <see cref="Vector{T}" /> from a given array.</summary>
        /// <param name="values">The array from which the vector is created.</param>
        /// <param name="index">The index in <paramref name="values" /> at which to being reading elements.</param>
        /// <returns>A new <see cref="Vector{T}" /> with its elements set to the first <see cref="Vector{T}.Count" /> elements from <paramref name="values" />.</returns>
        /// <exception cref="NullReferenceException"><paramref name="values" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The length of <paramref name="values" />, starting from <paramref name="index" />, is less than <see cref="Vector{T}.Count" />.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector(T[] values, int index)
        {
            // We explicitly don't check for `null` because historically this has thrown `NullReferenceException` for perf reasons

            if ((index < 0) || ((values.Length - index) < Count))
            {
                ThrowHelper.ThrowArgumentOutOfRange_IndexMustBeLessOrEqualException();
            }

            this = Unsafe.ReadUnaligned<Vector<T>>(ref Unsafe.As<T, byte>(ref values[index]));
        }

        /// <summary>Creates a new <see cref="Vector{T}" /> from a given readonly span.</summary>
        /// <param name="values">The readonly span from which the vector is created.</param>
        /// <returns>A new <see cref="Vector{T}" /> with its elements set to the first <see cref="Vector{T}.Count" /> elements from <paramref name="values" />.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The length of <paramref name="values" /> is less than <see cref="Vector{T}.Count" />.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector(ReadOnlySpan<T> values)
        {
            // We explicitly don't check for `null` because historically this has thrown `NullReferenceException` for perf reasons

            if (values.Length < Count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.values);
            }

            this = Unsafe.ReadUnaligned<Vector<T>>(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(values)));
        }

        /// <summary>Creates a new <see cref="Vector{T}" /> from a given readonly span.</summary>
        /// <param name="values">The readonly span from which the vector is created.</param>
        /// <returns>A new <see cref="Vector{T}" /> with its elements set to the first <c>sizeof(<see cref="Vector{T}" />)</c> elements from <paramref name="values" />.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The length of <paramref name="values" /> is less than <c>sizeof(<see cref="Vector{T}" />)</c>.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector(ReadOnlySpan<byte> values)
        {
            // We explicitly don't check for `null` because historically this has thrown `NullReferenceException` for perf reasons
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();

            if (values.Length < Vector<byte>.Count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.values);
            }

            this = Unsafe.ReadUnaligned<Vector<T>>(ref MemoryMarshal.GetReference(values));
        }

        /// <summary>Creates a new <see cref="Vector{T}" /> from a given span.</summary>
        /// <param name="values">The span from which the vector is created.</param>
        /// <returns>A new <see cref="Vector{T}" /> with its elements set to the first <see cref="Vector{T}.Count" /> elements from <paramref name="values" />.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The length of <paramref name="values" /> is less than <see cref="Vector{T}.Count" />.</exception>
        [OverloadResolutionPriority(-1)]
        public Vector(Span<T> values) : this((ReadOnlySpan<T>)values)
        {
        }

        /// <summary>Gets a new <see cref="Vector{T}" /> with all bits set to 1.</summary>
        /// <exception cref="NotSupportedException">The type of the current instance (<typeparamref name="T" />) is not supported.</exception>
        public static Vector<T> AllBitsSet
        {
            [Intrinsic]
            get => Vector.Create(Scalar<T>.AllBitsSet);
        }

        /// <summary>Gets the number of <typeparamref name="T" /> that are in a <see cref="Vector{T}" />.</summary>
        /// <exception cref="NotSupportedException">The type of the current instance (<typeparamref name="T" />) is not supported.</exception>
        public static int Count
        {
            [Intrinsic]
            get
            {
                ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
                return sizeof(Vector<T>) / sizeof(T);
            }
        }

        /// <summary>Gets a new <see cref="Vector{T}" /> with the elements set to their index.</summary>
        /// <exception cref="NotSupportedException">The type of the vector (<typeparamref name="T" />) is not supported.</exception>
        public static Vector<T> Indices
        {
            [Intrinsic]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
                Unsafe.SkipInit(out Vector<T> result);

                for (int i = 0; i < Count; i++)
                {
                    result.SetElementUnsafe(i, Scalar<T>.Convert(i));
                }

                return result;
            }
        }

        /// <summary>Gets <c>true</c> if <typeparamref name="T" /> is supported; otherwise, <c>false</c>.</summary>
        /// <returns><c>true</c> if <typeparamref name="T" /> is supported; otherwise, <c>false</c>.</returns>
        public static bool IsSupported
        {
            [Intrinsic]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (typeof(T) == typeof(byte)) ||
                   (typeof(T) == typeof(double)) ||
                   (typeof(T) == typeof(short)) ||
                   (typeof(T) == typeof(int)) ||
                   (typeof(T) == typeof(long)) ||
                   (typeof(T) == typeof(nint)) ||
                   (typeof(T) == typeof(nuint)) ||
                   (typeof(T) == typeof(sbyte)) ||
                   (typeof(T) == typeof(float)) ||
                   (typeof(T) == typeof(ushort)) ||
                   (typeof(T) == typeof(uint)) ||
                   (typeof(T) == typeof(ulong));
        }

        /// <summary>Gets a new <see cref="Vector{T}" /> with all elements initialized to one.</summary>
        /// <exception cref="NotSupportedException">The type of the current instance (<typeparamref name="T" />) is not supported.</exception>
        public static Vector<T> One
        {
            [Intrinsic]
            get => Vector.Create(Scalar<T>.One);
        }

        /// <summary>Gets a new <see cref="Vector{T}" /> with all elements initialized to zero.</summary>
        /// <exception cref="NotSupportedException">The type of the current instance (<typeparamref name="T" />) is not supported.</exception>
        public static Vector<T> Zero
        {
            [Intrinsic]
            get
            {
                ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
                return default;
            }
        }

        internal string DisplayString => IsSupported ? ToString() : SR.NotSupported_Type;

        /// <summary>Gets the element at the specified index.</summary>
        /// <param name="index">The index of the element to get.</param>
        /// <returns>The value of the element at <paramref name="index" />.</returns>
        /// <exception cref="NotSupportedException">The type of the current instance (<typeparamref name="T" />) is not supported.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> was less than zero or greater than the number of elements.</exception>
        public T this[int index]
        {
            [Intrinsic]
            get => this.GetElement(index);
        }

        /// <summary>Adds two vectors to compute their sum.</summary>
        /// <param name="left">The vector to add with <paramref name="right" />.</param>
        /// <param name="right">The vector to add with <paramref name="left" />.</param>
        /// <returns>The sum of <paramref name="left" /> and <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> operator +(Vector<T> left, Vector<T> right)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Count; index++)
            {
                T value = Scalar<T>.Add(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Computes the bitwise-and of two vectors.</summary>
        /// <param name="left">The vector to bitwise-and with <paramref name="right" />.</param>
        /// <param name="right">The vector to bitwise-and with <paramref name="left" />.</param>
        /// <returns>The bitwise-and of <paramref name="left" /> and <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> operator &(Vector<T> left, Vector<T> right)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
            Unsafe.SkipInit(out Vector<ulong> result);

            Vector<ulong> vleft = left.As<T, ulong>();
            Vector<ulong> vright = right.As<T, ulong>();

            for (int index = 0; index < Vector<ulong>.Count; index++)
            {
                ulong value = vleft.GetElementUnsafe(index) & vright.GetElementUnsafe(index);
                result.SetElementUnsafe(index, value);
            }

            return result.As<ulong, T>();
        }

        /// <summary>Computes the bitwise-or of two vectors.</summary>
        /// <param name="left">The vector to bitwise-or with <paramref name="right" />.</param>
        /// <param name="right">The vector to bitwise-or with <paramref name="left" />.</param>
        /// <returns>The bitwise-or of <paramref name="left" /> and <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> operator |(Vector<T> left, Vector<T> right)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
            Unsafe.SkipInit(out Vector<ulong> result);

            Vector<ulong> vleft = left.As<T, ulong>();
            Vector<ulong> vright = right.As<T, ulong>();

            for (int index = 0; index < Vector<ulong>.Count; index++)
            {
                ulong value = vleft.GetElementUnsafe(index) | vright.GetElementUnsafe(index);
                result.SetElementUnsafe(index, value);
            }

            return result.As<ulong, T>();
        }

        /// <summary>Divides two vectors to compute their quotient.</summary>
        /// <param name="left">The vector that will be divided by <paramref name="right" />.</param>
        /// <param name="right">The vector that will divide <paramref name="left" />.</param>
        /// <returns>The quotient of <paramref name="left" /> divided by <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> operator /(Vector<T> left, Vector<T> right)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Count; index++)
            {
                T value = Scalar<T>.Divide(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Divides a vector by a scalar to compute the per-element quotient.</summary>
        /// <param name="left">The vector that will be divided by <paramref name="right" />.</param>
        /// <param name="right">The scalar that will divide <paramref name="left" />.</param>
        /// <returns>The quotient of <paramref name="left" /> divided by <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> operator /(Vector<T> left, T right)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Count; index++)
            {
                T value = Scalar<T>.Divide(left.GetElementUnsafe(index), right);
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Compares two vectors to determine if all elements are equal.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if all elements in <paramref name="left" /> were equal to the corresponding element in <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector<T> left, Vector<T> right)
        {
            for (int index = 0; index < Count; index++)
            {
                if (!Scalar<T>.Equals(left.GetElementUnsafe(index), right.GetElementUnsafe(index)))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Computes the exclusive-or of two vectors.</summary>
        /// <param name="left">The vector to exclusive-or with <paramref name="right" />.</param>
        /// <param name="right">The vector to exclusive-or with <paramref name="left" />.</param>
        /// <returns>The exclusive-or of <paramref name="left" /> and <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> operator ^(Vector<T> left, Vector<T> right)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
            Unsafe.SkipInit(out Vector<ulong> result);

            Vector<ulong> vleft = left.As<T, ulong>();
            Vector<ulong> vright = right.As<T, ulong>();

            for (int index = 0; index < Vector<ulong>.Count; index++)
            {
                ulong value = vleft.GetElementUnsafe(index) ^ vright.GetElementUnsafe(index);
                result.SetElementUnsafe(index, value);
            }

            return result.As<ulong, T>();
        }

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;Byte&gt;" />.</summary>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;Byte&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static explicit operator Vector<byte>(Vector<T> value) => value.As<T, byte>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;Double&gt;" />.</summary>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;Double&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static explicit operator Vector<double>(Vector<T> value) => value.As<T, double>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;Int16&gt;" />.</summary>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;Int16&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static explicit operator Vector<short>(Vector<T> value) => value.As<T, short>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;Int32&gt;" />.</summary>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;Int32&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static explicit operator Vector<int>(Vector<T> value) => value.As<T, int>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;Int64&gt;" />.</summary>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;Int64&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static explicit operator Vector<long>(Vector<T> value) => value.As<T, long>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;IntPtr&gt;" />.</summary>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;IntPtr&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static explicit operator Vector<nint>(Vector<T> value) => value.As<T, nint>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;UIntPtr&gt;" />.</summary>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;UIntPtr&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static explicit operator Vector<nuint>(Vector<T> value) => value.As<T, nuint>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;SByte&gt;" />.</summary>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;SByte&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static explicit operator Vector<sbyte>(Vector<T> value) => value.As<T, sbyte>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;Single&gt;" />.</summary>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;Single&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static explicit operator Vector<float>(Vector<T> value) => value.As<T, float>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;UInt16&gt;" />.</summary>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;UInt16&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static explicit operator Vector<ushort>(Vector<T> value) => value.As<T, ushort>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;UInt32&gt;" />.</summary>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;UInt32&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static explicit operator Vector<uint>(Vector<T> value) => value.As<T, uint>();

        /// <summary>Reinterprets a <see cref="Vector{T}" /> as a new <see langword="Vector&lt;UInt64&gt;" />.</summary>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see langword="Vector&lt;UInt64&gt;" />.</returns>
        /// <exception cref="NotSupportedException">The type of <paramref name="value" /> (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        [CLSCompliant(false)]
        public static explicit operator Vector<ulong>(Vector<T> value) => value.As<T, ulong>();

        /// <summary>Compares two vectors to determine if any elements are not equal.</summary>
        /// <param name="left">The vector to compare with <paramref name="right" />.</param>
        /// <param name="right">The vector to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if any elements in <paramref name="left" /> was not equal to the corresponding element in <paramref name="right" />.</returns>
        [Intrinsic]
        public static bool operator !=(Vector<T> left, Vector<T> right) => !(left == right);

        /// <summary>Shifts each element of a vector left by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted left by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> operator <<(Vector<T> value, int shiftCount)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Count; index++)
            {
                T element = Scalar<T>.ShiftLeft(value.GetElementUnsafe(index), shiftCount);
                result.SetElementUnsafe(index, element);
            }

            return result;
        }

        /// <summary>Multiplies two vectors to compute their element-wise product.</summary>
        /// <param name="left">The vector to multiply with <paramref name="right" />.</param>
        /// <param name="right">The vector to multiply with <paramref name="left" />.</param>
        /// <returns>The element-wise product of <paramref name="left" /> and <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> operator *(Vector<T> left, Vector<T> right)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Count; index++)
            {
                T value = Scalar<T>.Multiply(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Multiplies a vector by a scalar to compute their product.</summary>
        /// <param name="value">The vector to multiply with <paramref name="factor" />.</param>
        /// <param name="factor">The scalar to multiply with <paramref name="value" />.</param>
        /// <returns>The product of <paramref name="value" /> and <paramref name="factor" />.</returns>
        [Intrinsic]
        public static Vector<T> operator *(Vector<T> value, T factor) => value * Vector.Create(factor);

        /// <summary>Multiplies a vector by a scalar to compute their product.</summary>
        /// <param name="factor">The scalar to multiply with <paramref name="value" />.</param>
        /// <param name="value">The vector to multiply with <paramref name="factor" />.</param>
        /// <returns>The product of <paramref name="factor" /> and <paramref name="value" />.</returns>
        [Intrinsic]
        public static Vector<T> operator *(T factor, Vector<T> value) => value * factor;

        /// <summary>Computes the ones-complement of a vector.</summary>
        /// <param name="value">The vector whose ones-complement is to be computed.</param>
        /// <returns>A vector whose elements are the ones-complement of the corresponding elements in <paramref name="value" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> operator ~(Vector<T> value)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
            Unsafe.SkipInit(out Vector<ulong> result);

            Vector<ulong> vector = value.As<T, ulong>();

            for (int index = 0; index < Vector<ulong>.Count; index++)
            {
                ulong element = ~vector.GetElementUnsafe(index);
                result.SetElementUnsafe(index, element);
            }

            return result.As<ulong, T>();
        }

        /// <summary>Shifts (signed) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> operator >>(Vector<T> value, int shiftCount)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Count; index++)
            {
                T element = Scalar<T>.ShiftRightArithmetic(value.GetElementUnsafe(index), shiftCount);
                result.SetElementUnsafe(index, element);
            }

            return result;
        }

        /// <summary>Subtracts two vectors to compute their difference.</summary>
        /// <param name="left">The vector from which <paramref name="right" /> will be subtracted.</param>
        /// <param name="right">The vector to subtract from <paramref name="left" />.</param>
        /// <returns>The difference of <paramref name="left" /> and <paramref name="right" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> operator -(Vector<T> left, Vector<T> right)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Count; index++)
            {
                T value = Scalar<T>.Subtract(left.GetElementUnsafe(index), right.GetElementUnsafe(index));
                result.SetElementUnsafe(index, value);
            }

            return result;
        }

        /// <summary>Computes the unary negation of a vector.</summary>
        /// <param name="value">The vector to negate.</param>
        /// <returns>A vector whose elements are the unary negation of the corresponding elements in <paramref name="value" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> operator -(Vector<T> value)
        {
            if (typeof(T) == typeof(float))
            {
                return value ^ Vector.Create(-0.0f).As<float, T>();
            }
            else if (typeof(T) == typeof(double))
            {
                return value ^ Vector.Create(-0.0).As<double, T>();
            }
            else
            {
                return Zero - value;
            }
        }

        /// <summary>Returns a given vector unchanged.</summary>
        /// <param name="value">The vector.</param>
        /// <returns><paramref name="value" /></returns>
        /// <exception cref="NotSupportedException">The type of the vector (<typeparamref name="T" />) is not supported.</exception>
        [Intrinsic]
        public static Vector<T> operator +(Vector<T> value)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();
            return value;
        }

        /// <summary>Shifts (unsigned) each element of a vector right by the specified amount.</summary>
        /// <param name="value">The vector whose elements are to be shifted.</param>
        /// <param name="shiftCount">The number of bits by which to shift each element.</param>
        /// <returns>A vector whose elements where shifted right by <paramref name="shiftCount" />.</returns>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> operator >>>(Vector<T> value, int shiftCount)
        {
            Unsafe.SkipInit(out Vector<T> result);

            for (int index = 0; index < Count; index++)
            {
                T element = Scalar<T>.ShiftRightLogical(value.GetElementUnsafe(index), shiftCount);
                result.SetElementUnsafe(index, element);
            }

            return result;
        }

        /// <summary>Copies a <see cref="Vector{T}" /> to a given array.</summary>
        /// <param name="destination">The array to which the current instance is copied.</param>
        /// <exception cref="NullReferenceException"><paramref name="destination" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The length of <paramref name="destination" /> is less than <see cref="Vector{T}.Count" />.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(T[] destination)
        {
            // We explicitly don't check for `null` because historically this has thrown `NullReferenceException` for perf reasons

            if (destination.Length < Count)
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }

            Unsafe.WriteUnaligned(ref Unsafe.As<T, byte>(ref destination[0]), this);
        }

        /// <summary>Copies a <see cref="Vector{T}" /> to a given array starting at the specified index.</summary>
        /// <param name="destination">The array to which the current instance is copied.</param>
        /// <param name="startIndex">The starting index of <paramref name="destination" /> which current instance will be copied to.</param>
        /// <exception cref="NullReferenceException"><paramref name="destination" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">The length of <paramref name="destination" /> is less than <see cref="Vector{T}.Count" />.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="startIndex" /> is negative or greater than the length of <paramref name="destination" />.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(T[] destination, int startIndex)
        {
            // We explicitly don't check for `null` because historically this has thrown `NullReferenceException` for perf reasons

            if ((uint)startIndex >= (uint)destination.Length)
            {
                ThrowHelper.ThrowStartIndexArgumentOutOfRange_ArgumentOutOfRange_IndexMustBeLess();
            }

            if ((destination.Length - startIndex) < Count)
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }

            Unsafe.WriteUnaligned(ref Unsafe.As<T, byte>(ref destination[startIndex]), this);
        }

        /// <summary>Copies a <see cref="Vector{T}" /> to a given span.</summary>
        /// <param name="destination">The span to which the current instance is copied.</param>
        /// <exception cref="ArgumentException">The length of <paramref name="destination" /> is less than <c>sizeof(<see cref="Vector{T}" />)</c>.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Span<byte> destination)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();

            if (destination.Length < Vector<byte>.Count)
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }

            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), this);
        }

        /// <summary>Copies a <see cref="Vector{T}" /> to a given span.</summary>
        /// <param name="destination">The span to which the current instance is copied.</param>
        /// <exception cref="ArgumentException">The length of <paramref name="destination" /> is less than <see cref="Vector{T}.Count" />.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Span<T> destination)
        {
            if (destination.Length < Count)
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }

            Unsafe.WriteUnaligned(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(destination)), this);
        }

        /// <summary>Returns a boolean indicating whether the given Object is equal to this vector instance.</summary>
        /// <param name="obj">The Object to compare against.</param>
        /// <returns>True if the Object is equal to this vector; False otherwise.</returns>
        public override bool Equals([NotNullWhen(true)] object? obj) => (obj is Vector<T> other) && Equals(other);

        /// <summary>Returns a boolean indicating whether the given vector is equal to this vector instance.</summary>
        /// <param name="other">The vector to compare this instance to.</param>
        /// <returns>True if the other vector is equal to this instance; False otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Vector<T> other)
        {
            // This function needs to account for floating-point equality around NaN
            // and so must behave equivalently to the underlying float/double.Equals

            if (Vector.IsHardwareAccelerated)
            {
                if ((typeof(T) == typeof(double)) || (typeof(T) == typeof(float)))
                {
                    Vector<T> result = Vector.Equals(this, other) | ~(Vector.Equals(this, this) | Vector.Equals(other, other));
                    return result.As<T, int>() == Vector<int>.AllBitsSet;
                }
                else
                {
                    return this == other;
                }
            }

            return SoftwareFallback(in this, other);

            static bool SoftwareFallback(in Vector<T> self, Vector<T> other)
            {
                for (int index = 0; index < Count; index++)
                {
                    if (!Scalar<T>.ObjectEquals(self.GetElementUnsafe(index), other.GetElementUnsafe(index)))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>Returns the hash code for this instance.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            HashCode hashCode = default;

            for (int index = 0; index < Count; index++)
            {
                T value = this.GetElementUnsafe(index);
                hashCode.Add(value);
            }

            return hashCode.ToHashCode();
        }

        /// <summary>Returns a String representing this vector.</summary>
        /// <returns>The string representation.</returns>
        public override string ToString() => ToString("G", CultureInfo.CurrentCulture);

        /// <summary>Returns a String representing this vector, using the specified format string to format individual elements.</summary>
        /// <param name="format">The format of individual elements.</param>
        /// <returns>The string representation.</returns>
        public string ToString([StringSyntax(StringSyntaxAttribute.NumericFormat)] string? format) => ToString(format, CultureInfo.CurrentCulture);

        /// <summary>Returns a String representing this vector, using the specified format string to format individual elements and the given IFormatProvider.</summary>
        /// <param name="format">The format of individual elements.</param>
        /// <param name="formatProvider">The format provider to use when formatting elements.</param>
        /// <returns>The string representation.</returns>
        public string ToString([StringSyntax(StringSyntaxAttribute.NumericFormat)] string? format, IFormatProvider? formatProvider)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();

            var sb = new ValueStringBuilder(stackalloc char[64]);
            string separator = NumberFormatInfo.GetInstance(formatProvider).NumberGroupSeparator;

            sb.Append('<');
            sb.Append(((IFormattable)this.GetElementUnsafe(0)).ToString(format, formatProvider));

            for (int i = 1; i < Count; i++)
            {
                sb.Append(separator);
                sb.Append(' ');
                sb.Append(((IFormattable)this.GetElementUnsafe(i)).ToString(format, formatProvider));
            }
            sb.Append('>');

            return sb.ToString();
        }

        /// <summary>Tries to copy a <see cref="Vector{T}" /> to a given span.</summary>
        /// <param name="destination">The span to which the current instance is copied.</param>
        /// <returns><c>true</c> if the current instance was successfully copied to <paramref name="destination" />; otherwise, <c>false</c> if the length of <paramref name="destination" /> is less than <c>sizeof(<see cref="Vector{T}" />)</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCopyTo(Span<byte> destination)
        {
            ThrowHelper.ThrowForUnsupportedNumericsVectorBaseType<T>();

            if (destination.Length < Vector<byte>.Count)
            {
                return false;
            }

            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(destination), this);
            return true;
        }

        /// <summary>Tries to copy a <see cref="Vector{T}" /> to a given span.</summary>
        /// <param name="destination">The span to which the current instance is copied.</param>
        /// <returns><c>true</c> if the current instance was successfully copied to <paramref name="destination" />; otherwise, <c>false</c> if the length of <paramref name="destination" /> is less than <see cref="Vector{T}.Count" />.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCopyTo(Span<T> destination)
        {
            if (destination.Length < Count)
            {
                return false;
            }

            Unsafe.WriteUnaligned(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(destination)), this);
            return true;
        }

        //
        // ISimdVector
        //

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Alignment" />
        static int ISimdVector<Vector<T>, T>.Alignment => Vector.Alignment;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.ElementCount" />
        static int ISimdVector<Vector<T>, T>.ElementCount => Vector<T>.Count;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsHardwareAccelerated" />
        static bool ISimdVector<Vector<T>, T>.IsHardwareAccelerated
        {
            [Intrinsic]
            get => Vector.IsHardwareAccelerated;
        }

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Abs(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Abs(Vector<T> vector) => Vector.Abs(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Add(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Add(Vector<T> left, Vector<T> right) => left + right;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.All(TSelf, T)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.All(Vector<T> vector, T value) => Vector.All(vector, value);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.AllWhereAllBitsSet(TSelf)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.AllWhereAllBitsSet(Vector<T> vector) => Vector.AllWhereAllBitsSet(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.AndNot(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.AndNot(Vector<T> left, Vector<T> right) => Vector.AndNot(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Any(TSelf, T)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.Any(Vector<T> vector, T value) => Vector.Any(vector, value);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.AnyWhereAllBitsSet(TSelf)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.AnyWhereAllBitsSet(Vector<T> vector) => Vector.AnyWhereAllBitsSet(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.BitwiseAnd(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.BitwiseAnd(Vector<T> left, Vector<T> right) => left & right;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.BitwiseOr(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.BitwiseOr(Vector<T> left, Vector<T> right) => left | right;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Ceiling(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Ceiling(Vector<T> vector) => Vector.Ceiling(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Clamp(TSelf, TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Clamp(Vector<T> value, Vector<T> min, Vector<T> max) => Vector.Clamp(value, min, max);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.ClampNative(TSelf, TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.ClampNative(Vector<T> value, Vector<T> min, Vector<T> max) => Vector.ClampNative(value, min, max);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.ConditionalSelect(TSelf, TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.ConditionalSelect(Vector<T> condition, Vector<T> left, Vector<T> right) => Vector.ConditionalSelect(condition, left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.CopySign(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.CopySign(Vector<T> value, Vector<T> sign) => Vector.CopySign(value, sign);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.CopyTo(TSelf, T[])" />
        static void ISimdVector<Vector<T>, T>.CopyTo(Vector<T> vector, T[] destination) => vector.CopyTo(destination);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.CopyTo(TSelf, T[], int)" />
        static void ISimdVector<Vector<T>, T>.CopyTo(Vector<T> vector, T[] destination, int startIndex) => vector.CopyTo(destination, startIndex);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.CopyTo(TSelf, Span{T})" />
        static void ISimdVector<Vector<T>, T>.CopyTo(Vector<T> vector, Span<T> destination) => vector.CopyTo(destination);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Count(TSelf, T)" />
        [Intrinsic]
        static int ISimdVector<Vector<T>, T>.Count(Vector<T> vector, T value) => Vector.Count(vector, value);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.CountWhereAllBitsSet(TSelf)" />
        [Intrinsic]
        static int ISimdVector<Vector<T>, T>.CountWhereAllBitsSet(Vector<T> vector) => Vector.CountWhereAllBitsSet(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Create(T)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Create(T value) => Vector.Create(value);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Create(T[])" />
        static Vector<T> ISimdVector<Vector<T>, T>.Create(T[] values) => new Vector<T>(values);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Create(T[], int)" />
        static Vector<T> ISimdVector<Vector<T>, T>.Create(T[] values, int index) => new Vector<T>(values, index);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Create(ReadOnlySpan{T})" />
        static Vector<T> ISimdVector<Vector<T>, T>.Create(ReadOnlySpan<T> values) => Vector.Create(values);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.CreateScalar(T)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.CreateScalar(T value) => Vector.CreateScalar(value);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.CreateScalarUnsafe(T)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.CreateScalarUnsafe(T value) => Vector.CreateScalarUnsafe(value);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Divide(TSelf, T)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Divide(Vector<T> left, Vector<T> right) => left / right;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Divide(TSelf, T)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Divide(Vector<T> left, T right) => left / right;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Dot(TSelf, TSelf)" />
        [Intrinsic]
        static T ISimdVector<Vector<T>, T>.Dot(Vector<T> left, Vector<T> right) => Vector.Dot(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Equals(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Equals(Vector<T> left, Vector<T> right) => Vector.Equals(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.EqualsAll(TSelf, TSelf)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.EqualsAll(Vector<T> left, Vector<T> right) => left == right;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.EqualsAny(TSelf, TSelf)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.EqualsAny(Vector<T> left, Vector<T> right) => Vector.EqualsAny(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Floor(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Floor(Vector<T> vector) => Vector.Floor(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.GetElement(TSelf, int)" />
        [Intrinsic]
        static T ISimdVector<Vector<T>, T>.GetElement(Vector<T> vector, int index) => vector.GetElement(index);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.GreaterThan(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.GreaterThan(Vector<T> left, Vector<T> right) => Vector.GreaterThan(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.GreaterThanAll(TSelf, TSelf)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.GreaterThanAll(Vector<T> left, Vector<T> right) => Vector.GreaterThanAll(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.GreaterThanAny(TSelf, TSelf)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.GreaterThanAny(Vector<T> left, Vector<T> right) => Vector.GreaterThanAny(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.GreaterThanOrEqual(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.GreaterThanOrEqual(Vector<T> left, Vector<T> right) => Vector.GreaterThanOrEqual(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.GreaterThanOrEqualAll(TSelf, TSelf)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.GreaterThanOrEqualAll(Vector<T> left, Vector<T> right) => Vector.GreaterThanOrEqualAll(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.GreaterThanOrEqualAny(TSelf, TSelf)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.GreaterThanOrEqualAny(Vector<T> left, Vector<T> right) => Vector.GreaterThanOrEqualAny(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IndexOf(TSelf, T)" />
        [Intrinsic]
        static int ISimdVector<Vector<T>, T>.IndexOf(Vector<T> vector, T value) => Vector.IndexOf(vector, value);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IndexOfWhereAllBitsSet(TSelf)" />
        [Intrinsic]
        static int ISimdVector<Vector<T>, T>.IndexOfWhereAllBitsSet(Vector<T> vector) => Vector.IndexOfWhereAllBitsSet(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsEvenInteger(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.IsEvenInteger(Vector<T> vector) => Vector.IsEvenInteger(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsFinite(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.IsFinite(Vector<T> vector) => Vector.IsFinite(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsInfinity(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.IsInfinity(Vector<T> vector) => Vector.IsInfinity(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsInteger(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.IsInteger(Vector<T> vector) => Vector.IsInteger(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsNaN(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.IsNaN(Vector<T> vector) => Vector.IsNaN(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsNegative(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.IsNegative(Vector<T> vector) => Vector.IsNegative(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsNegativeInfinity(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.IsNegativeInfinity(Vector<T> vector) => Vector.IsNegativeInfinity(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsNormal(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.IsNormal(Vector<T> vector) => Vector.IsNormal(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsOddInteger(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.IsOddInteger(Vector<T> vector) => Vector.IsOddInteger(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsPositive(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.IsPositive(Vector<T> vector) => Vector.IsPositive(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsPositiveInfinity(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.IsPositiveInfinity(Vector<T> vector) => Vector.IsPositiveInfinity(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsSubnormal(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.IsSubnormal(Vector<T> vector) => Vector.IsSubnormal(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.IsZero(TSelf)" />
        static Vector<T> ISimdVector<Vector<T>, T>.IsZero(Vector<T> vector) => Vector.IsZero(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.LastIndexOf(TSelf, T)" />
        [Intrinsic]
        static int ISimdVector<Vector<T>, T>.LastIndexOf(Vector<T> vector, T value) => Vector.LastIndexOf(vector, value);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.LastIndexOfWhereAllBitsSet(TSelf)" />
        [Intrinsic]
        static int ISimdVector<Vector<T>, T>.LastIndexOfWhereAllBitsSet(Vector<T> vector) => Vector.LastIndexOfWhereAllBitsSet(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.LessThan(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.LessThan(Vector<T> left, Vector<T> right) => Vector.LessThan(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.LessThanAll(TSelf, TSelf)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.LessThanAll(Vector<T> left, Vector<T> right) => Vector.LessThanAll(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.LessThanAny(TSelf, TSelf)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.LessThanAny(Vector<T> left, Vector<T> right) => Vector.LessThanAny(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.LessThanOrEqual(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.LessThanOrEqual(Vector<T> left, Vector<T> right) => Vector.LessThanOrEqual(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.LessThanOrEqualAll(TSelf, TSelf)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.LessThanOrEqualAll(Vector<T> left, Vector<T> right) => Vector.LessThanOrEqualAll(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.LessThanOrEqualAny(TSelf, TSelf)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.LessThanOrEqualAny(Vector<T> left, Vector<T> right) => Vector.LessThanOrEqualAny(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Load(T*)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Load(T* source) => Vector.Load(source);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.LoadAligned(T*)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.LoadAligned(T* source) => Vector.LoadAligned(source);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.LoadAlignedNonTemporal(T*)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.LoadAlignedNonTemporal(T* source) => Vector.LoadAlignedNonTemporal(source);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.LoadUnsafe(ref readonly T)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.LoadUnsafe(ref readonly T source) => Vector.LoadUnsafe(in source);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.LoadUnsafe(ref readonly T, nuint)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.LoadUnsafe(ref readonly T source, nuint elementOffset) => Vector.LoadUnsafe(in source, elementOffset);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Max(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Max(Vector<T> left, Vector<T> right) => Vector.Max(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.MaxMagnitude(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.MaxMagnitude(Vector<T> left, Vector<T> right) => Vector.MaxMagnitude(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.MaxMagnitudeNumber(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.MaxMagnitudeNumber(Vector<T> left, Vector<T> right) => Vector.MaxMagnitudeNumber(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.MaxNative(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.MaxNative(Vector<T> left, Vector<T> right) => Vector.MaxNative(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.MaxNumber(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.MaxNumber(Vector<T> left, Vector<T> right) => Vector.MaxNumber(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Min(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Min(Vector<T> left, Vector<T> right) => Vector.Min(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.MinMagnitude(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.MinMagnitude(Vector<T> left, Vector<T> right) => Vector.MinMagnitude(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.MinMagnitudeNumber(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.MinMagnitudeNumber(Vector<T> left, Vector<T> right) => Vector.MinMagnitudeNumber(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.MinNative(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.MinNative(Vector<T> left, Vector<T> right) => Vector.MinNative(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.MinNumber(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.MinNumber(Vector<T> left, Vector<T> right) => Vector.MinNumber(left, right);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Multiply(TSelf, T)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Multiply(Vector<T> left, Vector<T> right) => left * right;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Multiply(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Multiply(Vector<T> left, T right) => left * right;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.MultiplyAddEstimate(TSelf, TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.MultiplyAddEstimate(Vector<T> left, Vector<T> right, Vector<T> addend) => Vector.MultiplyAddEstimate(left, right, addend);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Negate(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Negate(Vector<T> vector) => -vector;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.None(TSelf, T)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.None(Vector<T> vector, T value) => Vector.None(vector, value);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.NoneWhereAllBitsSet(TSelf)" />
        [Intrinsic]
        static bool ISimdVector<Vector<T>, T>.NoneWhereAllBitsSet(Vector<T> vector) => Vector.NoneWhereAllBitsSet(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.OnesComplement(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.OnesComplement(Vector<T> vector) => ~vector;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Round(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Round(Vector<T> vector) => Vector.Round(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.ShiftLeft(TSelf, int)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.ShiftLeft(Vector<T> vector, int shiftCount) => vector << shiftCount;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.ShiftRightArithmetic(TSelf, int)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.ShiftRightArithmetic(Vector<T> vector, int shiftCount) => vector >> shiftCount;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.ShiftRightLogical(TSelf, int)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.ShiftRightLogical(Vector<T> vector, int shiftCount) => vector >>> shiftCount;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Sqrt(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Sqrt(Vector<T> vector) => Vector.SquareRoot(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Store(TSelf, T*)" />
        [Intrinsic]
        static void ISimdVector<Vector<T>, T>.Store(Vector<T> source, T* destination) => source.Store(destination);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.StoreAligned(TSelf, T*)" />
        [Intrinsic]
        static void ISimdVector<Vector<T>, T>.StoreAligned(Vector<T> source, T* destination) => source.StoreAligned(destination);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.StoreAlignedNonTemporal(TSelf, T*)" />
        [Intrinsic]
        static void ISimdVector<Vector<T>, T>.StoreAlignedNonTemporal(Vector<T> source, T* destination) => source.StoreAlignedNonTemporal(destination);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.StoreUnsafe(TSelf, ref T)" />
        [Intrinsic]
        static void ISimdVector<Vector<T>, T>.StoreUnsafe(Vector<T> vector, ref T destination) => vector.StoreUnsafe(ref destination);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.StoreUnsafe(TSelf, ref T, nuint)" />
        [Intrinsic]
        static void ISimdVector<Vector<T>, T>.StoreUnsafe(Vector<T> vector, ref T destination, nuint elementOffset) => vector.StoreUnsafe(ref destination, elementOffset);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Subtract(TSelf, TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Subtract(Vector<T> left, Vector<T> right) => left - right;

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Sum(TSelf)" />
        [Intrinsic]
        static T ISimdVector<Vector<T>, T>.Sum(Vector<T> vector) => Vector.Sum(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.ToScalar(TSelf)" />
        [Intrinsic]
        static T ISimdVector<Vector<T>, T>.ToScalar(Vector<T> vector) => vector.ToScalar();

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Truncate(TSelf)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Truncate(Vector<T> vector) => Vector.Truncate(vector);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.TryCopyTo(TSelf, Span{T})" />
        static bool ISimdVector<Vector<T>, T>.TryCopyTo(Vector<T> vector, Span<T> destination) => vector.TryCopyTo(destination);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.WithElement(TSelf, int, T)" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.WithElement(Vector<T> vector, int index, T value) => vector.WithElement(index, value);

        /// <inheritdoc cref="ISimdVector{TSelf, T}.Xor" />
        [Intrinsic]
        static Vector<T> ISimdVector<Vector<T>, T>.Xor(Vector<T> left, Vector<T> right) => left ^ right;
    }
    public static class BitOperations
    {
        private static ReadOnlySpan<byte> TrailingZeroCountDeBruijn => // 32
        [
            00, 01, 28, 02, 29, 14, 24, 03,
            30, 22, 20, 15, 25, 17, 04, 08,
            31, 27, 13, 23, 21, 19, 16, 07,
            26, 12, 18, 06, 11, 05, 10, 09
        ];

        private static ReadOnlySpan<byte> Log2DeBruijn => // 32
        [
            00, 09, 01, 10, 13, 21, 02, 29,
            11, 14, 16, 18, 22, 25, 03, 30,
            08, 12, 20, 28, 15, 17, 24, 07,
            19, 27, 23, 06, 26, 05, 04, 31
        ];

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(nuint value)
        {
#if TARGET_64BIT
            return Log2((ulong)value);
#else
            return Log2((uint)value);
#endif
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(ulong value)
        {
            value |= 1;

            uint hi = (uint)(value >> 32);

            if (hi == 0)
            {
                return Log2((uint)value);
            }

            return 32 + Log2(hi);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(uint value)
        {
            // The 0->0 contract is fulfilled by setting the LSB to 1.
            // Log(1) is 0, and setting the LSB for values > 1 does not change the log2 result.
            value |= 1;

            // Fill trailing zeros with ones, eg 00010010 becomes 00011111
            value |= value >> 01;
            value |= value >> 02;
            value |= value >> 04;
            value |= value >> 08;
            value |= value >> 16;

            // Using deBruijn sequence, k=2, n=5 (2^5=32) : 0b_0000_0111_1100_0100_1010_1100_1101_1101u
            return Log2DeBruijn[(int)((value * 0x07C4ACDDu) >> 27)];
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Log2Ceiling(uint value)
        {
            int result = Log2(value);
            if (PopCount(value) != 1)
            {
                result++;
            }
            return result;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Log2Ceiling(ulong value)
        {
            int result = Log2(value);
            if (PopCount(value) != 1)
            {
                result++;
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(uint value)
        {
            const uint c1 = 0x_55555555u;
            const uint c2 = 0x_33333333u;
            const uint c3 = 0x_0F0F0F0Fu;
            const uint c4 = 0x_01010101u;

            value -= (value >> 1) & c1;
            value = (value & c2) + ((value >> 2) & c2);
            value = (((value + (value >> 4)) & c3) * c4) >> 24;

            return (int)value;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(ulong value)
        {
#if TARGET_64BIT
            const ulong c1 = 0x_55555555_55555555ul;
            const ulong c2 = 0x_33333333_33333333ul;
            const ulong c3 = 0x_0F0F0F0F_0F0F0F0Ful;
            const ulong c4 = 0x_01010101_01010101ul;

            value -= (value >> 1) & c1;
            value = (value & c2) + ((value >> 2) & c2);
            value = (((value + (value >> 4)) & c3) * c4) >> 56;

            return (int)value;
#else
            return PopCount((uint)value) // lo
                + PopCount((uint)(value >> 32)); // hi
#endif
        }
        public static int PopCount(nuint value)
        {
#if TARGET_64BIT
            return PopCount((ulong)value);
#else
            return PopCount((uint)value);
#endif
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint ResetLowestSetBit(uint value)
        {
            // It's lowered to BLSR on x86
            return value & (value - 1);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong ResetLowestSetBit(ulong value)
        {
            return value & (value - 1);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint FlipBit(uint value, int index)
        {
            return value ^ (1u << index);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong FlipBit(ulong value, int index)
        {
            return value ^ (1ul << index);
        }
    

        public static int LeadingZeroCount(uint value) => value == 0 ? 32 : 31 ^ Log2(value);

        public static int LeadingZeroCount(ulong value) => value == 0 ? 64 : 63 ^ Log2(value);

        public static int LeadingZeroCount(nuint value) => IntPtr.Size == 8 ? LeadingZeroCount((ulong)value) : LeadingZeroCount((uint)value);

        public static int TrailingZeroCount(int value) => TrailingZeroCount((uint)value);

        public static int TrailingZeroCount(uint value)
        {
            if (value == 0)
                return 32;
            return Unsafe.AddByteOffset(
                ref MemoryMarshal.GetReference(TrailingZeroCountDeBruijn),
                (IntPtr)(int)(((value & (uint)-(int)value) * 0x077CB531u) >> 27));
        }

        public static int TrailingZeroCount(long value) => TrailingZeroCount((ulong)value);

        public static int TrailingZeroCount(ulong value)
        {
            uint lo = (uint)value;
            if (lo == 0)
                return 32 + TrailingZeroCount((uint)(value >> 32));
            return TrailingZeroCount(lo);
        }

        public static int TrailingZeroCount(nint value) => TrailingZeroCount((nuint)value);

        public static int TrailingZeroCount(nuint value) => IntPtr.Size == 8 ? TrailingZeroCount((ulong)value) : TrailingZeroCount((uint)value);

        public static nuint RotateLeft(nuint value, int offset) => IntPtr.Size == 8 ? (nuint)RotateLeft((ulong)value, offset) : RotateLeft((uint)value, offset);

        public static nuint RotateRight(nuint value, int offset) => IntPtr.Size == 8 ? (nuint)RotateRight((ulong)value, offset) : RotateRight((uint)value, offset);
        /// <summary>
        /// Returns the integer (floor) log of the specified value, base 2.
        /// Note that by convention, input value 0 returns 0 since Log(0) is undefined.
        /// Does not directly use any hardware intrinsics, nor does it incur branching.
        /// </summary>
        /// <param name="value">The value.</param>
        private static int Log2SoftwareFallback(uint value)
        {
            // No AggressiveInlining due to large method size
            // Has conventional contract 0->0 (Log(0) is undefined)

            // Fill trailing zeros with ones, eg 00010010 becomes 00011111
            value |= value >> 01;
            value |= value >> 02;
            value |= value >> 04;
            value |= value >> 08;
            value |= value >> 16;

            // Using deBruijn sequence, k=2, n=5 (2^5=32) : 0b_0000_0111_1100_0100_1010_1100_1101_1101u
            return Log2DeBruijn[(int)((value * 0x07C4ACDDu) >> 27)];
        }
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
            => new Vector2(left.X + right.X, left.Y + right.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector2 value)
        {
            this.X += value.X;
            this.Y += value.Y;
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
        public void operator *=(Vector2 value)
        {
            this.X *= value.X;
            this.Y *= value.Y;
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
        public void operator -=(Vector2 value)
        {
            this.X -= value.X;
            this.Y -= value.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator -(Vector2 value) => new Vector2(-(value.X), -(value.Y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator &(Vector2 left, Vector2 right)
            => new Vector2(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) & Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) & Unsafe.BitCast<float, int>(right.Y)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator |(Vector2 left, Vector2 right)
            => new Vector2(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) | Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) | Unsafe.BitCast<float, int>(right.Y)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator ^(Vector2 left, Vector2 right)
            => new Vector2(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) ^ Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) ^ Unsafe.BitCast<float, int>(right.Y)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator <<(Vector2 value, int shiftAmount)
            => new Vector2(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) << shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) << shiftAmount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator ~(Vector2 value)
            => new Vector2(
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.X)),
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.Y)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator >>(Vector2 value, int shiftAmount)
            => new Vector2(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) >> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) >> shiftAmount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator +(Vector2 value) => value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 operator >>>(Vector2 value, int shiftAmount)
            => new Vector2(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) >>> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) >>> shiftAmount));

        public static float Distance(Vector2 value1, Vector2 value2) => float.Sqrt(DistanceSquared(value1, value2));
        public static float DistanceSquared(Vector2 value1, Vector2 value2) => (value1 - value2).LengthSquared();

        public static Vector2 Divide(Vector2 left, Vector2 right) => left / right;
        public static Vector2 Divide(Vector2 left, float divisor) => left / divisor;

        public static Vector2 Multiply(Vector2 left, Vector2 right) => left * right;
        public static Vector2 Multiply(Vector2 left, float right) => left * right;
        public static Vector2 Multiply(float left, Vector2 right) => left * right;

        public static Vector2 Negate(Vector2 value) => -value;

        public static Vector2 Subtract(Vector2 left, Vector2 right) => left - right;
        public static Vector2 Add(Vector2 left, Vector2 right) => left + right;

        public static Vector2 Xor(Vector2 left, Vector2 right) => left ^ right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vector2 vector1, Vector2 vector2)
            => vector1.X * vector2.X + vector1.Y * vector2.Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Cross(Vector2 value1, Vector2 value2)
            => value1.X * value2.Y - value1.Y * value2.X;

        public static (Vector2 Sin, Vector2 Cos) SinCos(Vector2 vector)
        {
            var (sinX, cosX) = MathF.SinCos(vector.X);
            var (sinY, cosY) = MathF.SinCos(vector.Y);

            return (
                new Vector2(sinX, sinY),
                new Vector2(cosX, cosY)
            );
        }

        public override readonly int GetHashCode() => HashCode.Combine(X, Y);

        public readonly float Length() => float.Sqrt(LengthSquared());
        public readonly float LengthSquared() => Dot(this, this);

        public static Vector2 Normalize(Vector2 value) => value / value.Length();
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
            => new Vector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector3 value)
        {
            this.X += value.X;
            this.Y += value.Y;
            this.Z += value.Z;
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
        public void operator *=(Vector3 value)
        {
            this.X *= value.X;
            this.Y *= value.Y;
            this.Z *= value.Z;
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
        public void operator -=(Vector3 value)
        {
            this.X -= value.X;
            this.Y -= value.Y;
            this.Z -= value.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator -(Vector3 value) => new Vector3(-(value.X), -(value.Y), -(value.Z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator &(Vector3 left, Vector3 right)
             => new Vector3(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) & Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) & Unsafe.BitCast<float, int>(right.Y)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Z) & Unsafe.BitCast<float, int>(right.Z)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator |(Vector3 left, Vector3 right)
             => new Vector3(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) | Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) | Unsafe.BitCast<float, int>(right.Y)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Z) | Unsafe.BitCast<float, int>(right.Z)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator ^(Vector3 left, Vector3 right)
             => new Vector3(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) ^ Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) ^ Unsafe.BitCast<float, int>(right.Y)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Z) ^ Unsafe.BitCast<float, int>(right.Z)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator <<(Vector3 value, int shiftAmount)
            => new Vector3(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) << shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) << shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Z) << shiftAmount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator ~(Vector3 value)
            => new Vector3(
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.X)),
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.Y)),
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.Z)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator >>(Vector3 value, int shiftAmount)
            => new Vector3(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) >> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) >> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Z) >> shiftAmount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator +(Vector3 value) => value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator >>>(Vector3 value, int shiftAmount)
            => new Vector3(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) >>> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) >>> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Z) >>> shiftAmount));

        public static float Distance(Vector3 value1, Vector3 value2) => float.Sqrt(DistanceSquared(value1, value2));
        public static float DistanceSquared(Vector3 value1, Vector3 value2) => (value1 - value2).LengthSquared();

        public static Vector3 Divide(Vector3 left, Vector3 right) => left / right;
        public static Vector3 Divide(Vector3 left, float divisor) => left / divisor;

        public static Vector3 Multiply(Vector3 left, Vector3 right) => left * right;
        public static Vector3 Multiply(Vector3 left, float right) => left * right;
        public static Vector3 Multiply(float left, Vector3 right) => left * right;

        public static Vector3 Negate(Vector3 value) => -value;

        public static Vector3 Subtract(Vector3 left, Vector3 right) => left - right;
        public static Vector3 Add(Vector3 left, Vector3 right) => left + right;

        public static Vector3 Xor(Vector3 left, Vector3 right) => left ^ right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vector3 vector1, Vector3 vector2)
            => vector1.X * vector2.X + vector1.Y * vector2.Y + vector1.Z * vector2.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Cross(Vector3 vector1, Vector3 vector2)
            => new Vector3(vector1.Y * vector2.Z - vector1.Z * vector2.Y,
                           vector1.Z * vector2.X - vector1.X * vector2.Z,
                           vector1.X * vector2.Y - vector1.Y * vector2.X);

        public static (Vector3 Sin, Vector3 Cos) SinCos(Vector3 vector)
        {
            var (sinX, cosX) = MathF.SinCos(vector.X);
            var (sinY, cosY) = MathF.SinCos(vector.Y);
            var (sinZ, cosZ) = MathF.SinCos(vector.Z);

            return (
                new Vector3(sinX, sinY, sinZ),
                new Vector3(cosX, cosY, cosZ)
            );
        }

        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);

        public readonly float Length() => float.Sqrt(LengthSquared());
        public readonly float LengthSquared() => Dot(this, this);

        public static Vector3 Normalize(Vector3 value) => value / value.Length();
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
            => new Vector4(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void operator +=(Vector4 value)
        {
            this.X += value.X;
            this.Y += value.Y;
            this.Z += value.Z;
            this.W += value.W;
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
        public void operator *=(Vector4 value)
        {
            this.X *= value.X;
            this.Y *= value.Y;
            this.Z *= value.Z;
            this.W *= value.W;
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
        public void operator -=(Vector4 value)
        {
            this.X -= value.X;
            this.Y -= value.Y;
            this.Z -= value.Z;
            this.W -= value.W;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator -(Vector4 value) => new Vector4(-(value.X), -(value.Y), -(value.Z), -(value.W));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator &(Vector4 left, Vector4 right)
            => new Vector4(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) & Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) & Unsafe.BitCast<float, int>(right.Y)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Z) & Unsafe.BitCast<float, int>(right.Z)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.W) & Unsafe.BitCast<float, int>(right.W)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator |(Vector4 left, Vector4 right)
            => new Vector4(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) | Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) | Unsafe.BitCast<float, int>(right.Y)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Z) | Unsafe.BitCast<float, int>(right.Z)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.W) | Unsafe.BitCast<float, int>(right.W)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator ^(Vector4 left, Vector4 right)
            => new Vector4(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.X) ^ Unsafe.BitCast<float, int>(right.X)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Y) ^ Unsafe.BitCast<float, int>(right.Y)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.Z) ^ Unsafe.BitCast<float, int>(right.Z)),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(left.W) ^ Unsafe.BitCast<float, int>(right.W)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator <<(Vector4 value, int shiftAmount)
            => new Vector4(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) << shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) << shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Z) << shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.W) << shiftAmount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator ~(Vector4 value)
            => new Vector4(
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.X)),
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.Y)),
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.Z)),
                Unsafe.BitCast<int, float>(~Unsafe.BitCast<float, int>(value.W)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator >>(Vector4 value, int shiftAmount)
            => new Vector4(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) >> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) >> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Z) >> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.W) >> shiftAmount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator +(Vector4 value) => value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator >>>(Vector4 value, int shiftAmount)
            => new Vector4(
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.X) >>> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Y) >>> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.Z) >>> shiftAmount),
                Unsafe.BitCast<int, float>(Unsafe.BitCast<float, int>(value.W) >>> shiftAmount));

        public static float Distance(Vector4 value1, Vector4 value2) => float.Sqrt(DistanceSquared(value1, value2));
        public static float DistanceSquared(Vector4 value1, Vector4 value2) => (value1 - value2).LengthSquared();

        public static Vector4 Divide(Vector4 left, Vector4 right) => left / right;
        public static Vector4 Divide(Vector4 left, float divisor) => left / divisor;

        public static Vector4 Multiply(Vector4 left, Vector4 right) => left * right;
        public static Vector4 Multiply(Vector4 left, float right) => left * right;
        public static Vector4 Multiply(float left, Vector4 right) => left * right;

        public static Vector4 Negate(Vector4 value) => -value;

        public static Vector4 Subtract(Vector4 left, Vector4 right) => left - right;
        public static Vector4 Add(Vector4 left, Vector4 right) => left + right;

        public static Vector4 Xor(Vector4 left, Vector4 right) => left ^ right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vector4 vector1, Vector4 vector2)
            => vector1.X * vector2.X + vector1.Y * vector2.Y + vector1.Z * vector2.Z + vector1.W * vector2.W;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Cross(Vector4 vector1, Vector4 vector2)
            => new Vector4(vector1.Y * vector2.Z - vector1.Z * vector2.Y,
                           vector1.Z * vector2.X - vector1.X * vector2.Z,
                           vector1.X * vector2.Y - vector1.Y * vector2.X,
                           vector1.W * vector2.W);

        public static (Vector4 Sin, Vector4 Cos) SinCos(Vector4 vector)
        {
            var (sinX, cosX) = MathF.SinCos(vector.X);
            var (sinY, cosY) = MathF.SinCos(vector.Y);
            var (sinZ, cosZ) = MathF.SinCos(vector.Z);
            var (sinW, cosW) = MathF.SinCos(vector.W);

            return (
                new Vector4(sinX, sinY, sinZ, sinW),
                new Vector4(cosX, cosY, cosZ, cosW)
            );
        }

        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);

        public readonly float Length() => float.Sqrt(LengthSquared());
        public readonly float LengthSquared() => Dot(this, this);

        public static Vector4 Normalize(Vector4 value) => value / value.Length();
    }

    public struct Quaternion : IEquatable<Quaternion>
    {
        /// <summary>The X value of the vector component of the quaternion.</summary>
        public float X;

        /// <summary>The Y value of the vector component of the quaternion.</summary>
        public float Y;

        /// <summary>The Z value of the vector component of the quaternion.</summary>
        public float Z;

        /// <summary>The rotation component of the quaternion.</summary>
        public float W;

        internal const int Count = 4;

        public Quaternion(float x, float y, float z, float w)
        {
            X = x; Y = y; Z = z; W = w;
        }
        public Quaternion(Vector3 vectorPart, float scalarPart)
        {
            this = Create(vectorPart, scalarPart);
        }

        public static Quaternion Zero
        {
            get => default;
        }
        public static Quaternion Identity
        {
            get => Create(0.0f, 0.0f, 0.0f, 1.0f);
        }

        public static Quaternion Create(float x, float y, float z, float w) => new Quaternion(x, y, z, w);
        public static Quaternion Create(Vector3 vectorPart, float scalarPart) => new Quaternion(vectorPart.X, vectorPart.Y, vectorPart.Z, scalarPart);

        public static Quaternion CreateFromRotationMatrix(Matrix4x4 matrix)
        {
            float trace = matrix.M11 + matrix.M22 + matrix.M33;

            Quaternion q = default;

            if (trace > 0.0f)
            {
                float s = float.Sqrt(trace + 1.0f);
                q.W = s * 0.5f;
                s = 0.5f / s;
                q.X = (matrix.M23 - matrix.M32) * s;
                q.Y = (matrix.M31 - matrix.M13) * s;
                q.Z = (matrix.M12 - matrix.M21) * s;
            }
            else
            {
                if (matrix.M11 >= matrix.M22 && matrix.M11 >= matrix.M33)
                {
                    float s = float.Sqrt(1.0f + matrix.M11 - matrix.M22 - matrix.M33);
                    float invS = 0.5f / s;
                    q.X = 0.5f * s;
                    q.Y = (matrix.M12 + matrix.M21) * invS;
                    q.Z = (matrix.M13 + matrix.M31) * invS;
                    q.W = (matrix.M23 - matrix.M32) * invS;
                }
                else if (matrix.M22 > matrix.M33)
                {
                    float s = float.Sqrt(1.0f + matrix.M22 - matrix.M11 - matrix.M33);
                    float invS = 0.5f / s;
                    q.X = (matrix.M21 + matrix.M12) * invS;
                    q.Y = 0.5f * s;
                    q.Z = (matrix.M32 + matrix.M23) * invS;
                    q.W = (matrix.M31 - matrix.M13) * invS;
                }
                else
                {
                    float s = float.Sqrt(1.0f + matrix.M33 - matrix.M11 - matrix.M22);
                    float invS = 0.5f / s;
                    q.X = (matrix.M31 + matrix.M13) * invS;
                    q.Y = (matrix.M32 + matrix.M23) * invS;
                    q.Z = 0.5f * s;
                    q.W = (matrix.M12 - matrix.M21) * invS;
                }
            }

            return q;
        }
        public static Quaternion CreateFromYawPitchRoll(float yaw, float pitch, float roll)
        {
            (Vector3 sin, Vector3 cos) = Vector3.SinCos((new Vector3(roll, pitch, yaw)) * 0.5f);

            (float sr, float cr) = (sin.X, cos.X);
            (float sp, float cp) = (sin.Y, cos.Y);
            (float sy, float cy) = (sin.Z, cos.Z);

            Quaternion result;

            result.X = cy * sp * cr + sy * cp * sr;
            result.Y = sy * cp * cr - cy * sp * sr;
            result.Z = cy * cp * sr - sy * sp * cr;
            result.W = cy * cp * cr + sy * sp * sr;

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Quaternion quaternion1, Quaternion quaternion2)
            => quaternion1.X * quaternion2.X +
               quaternion1.Y * quaternion2.Y +
               quaternion1.Z * quaternion2.Z +
               quaternion1.W * quaternion2.W;

        public static bool operator ==(Quaternion value1, Quaternion value2)
            => value1.X == value2.X && value1.Y == value2.Y && value1.Z == value2.Z && value1.W == value2.W;
        public static bool operator !=(Quaternion value1, Quaternion value2) => !(value1 == value2);

        public readonly float Length() => float.Sqrt(LengthSquared());
        public readonly float LengthSquared() => Dot(this, this);

        public override readonly bool Equals([NotNullWhen(true)] object? obj) => (obj is Quaternion other) && Equals(other);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(Quaternion other) => this == other;
        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);
        public override readonly string ToString() => $"{{X:{X} Y:{Y} Z:{Z} W:{W}}}";
    }

    public struct Matrix3x2
    {
        private const int RowCount = 3;
        private const int ColumnCount = 2;

        public float M11;
        public float M12;
        public float M21;
        public float M22;
        public float M31;
        public float M32;

        public Matrix3x2(float m11, float m12,
                         float m21, float m22,
                         float m31, float m32)
        {
            M11 = m11; M12 = m12;
            M21 = m21; M22 = m22;
            M31 = m31; M32 = m32;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x2 Create(float m11, float m12,
                                       float m21, float m22,
                                       float m31, float m32)
            => new Matrix3x2(m11, m12, m21, m22, m31, m32);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x2 Create(Vector2 x, Vector2 y, Vector2 z)
            => new Matrix3x2(x.X, x.Y, y.X, y.Y, z.X, z.Y);

        public Vector2 X
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector2(M11, M12);
        }
        public Vector2 Y
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector2(M21, M22);
        }
        public Vector2 Z
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector2(M31, M32);
        }
    }
    public struct Matrix4x4
    {
        internal const int RowCount = 4;
        internal const int ColumnCount = 4;

        public float M11;
        public float M12;
        public float M13;
        public float M14;

        public float M21;
        public float M22;
        public float M23;
        public float M24;

        public float M31;
        public float M32;
        public float M33;
        public float M34;

        public float M41;
        public float M42;
        public float M43;
        public float M44;

        public Matrix4x4(float m11, float m12, float m13, float m14,
                         float m21, float m22, float m23, float m24,
                         float m31, float m32, float m33, float m34,
                         float m41, float m42, float m43, float m44)
        {
            M11 = m11; M12 = m12; M13 = m13; M14 = m14;
            M21 = m21; M22 = m22; M23 = m23; M24 = m24;
            M31 = m31; M32 = m32; M33 = m33; M34 = m34;
            M41 = m41; M42 = m42; M43 = m43; M44 = m44;
        }
        public static Matrix4x4 Identity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Create(Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ, Vector4.UnitW);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 Create(Vector4 x, Vector4 y, Vector4 z, Vector4 w)
            => new Matrix4x4(x.X, x.Y, x.Z, x.W,
                             y.X, y.Y, y.Z, y.W,
                             z.X, z.Y, z.Z, z.W,
                             w.X, w.Y, w.Z, w.W);
        public Vector4 X
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector4(M11, M12, M13, M14);
        }
        public Vector4 Y
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector4(M21, M22, M23, M24);
        }
        public Vector4 Z
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector4(M31, M32, M33, M34);
        }
        public Vector4 W
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => new Vector4(M41, M42, M43, M44);
        }
    }
}