using System;
using System.Collections.Immutable;
using System.IO;
using System.Numerics;

namespace Cnidaria.Python
{
    public sealed class PycOptions
    {
        public uint SourceTimestamp { get; init; }
        public uint SourceSize { get; init; }
    }

    public static class PycWriter
    {
        public static ImmutableArray<byte> Write(
            PythonCodeObject codeObject,
            PycOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(codeObject);
            if (codeObject.Version != PythonBytecodeVersion.CPython3_14_6)
                throw new NotSupportedException($"Unsupported bytecode version {codeObject.Version}.");

            options ??= new PycOptions();
            using var stream = new MemoryStream();
            Write(stream, codeObject, options);
            return ImmutableArray.CreateRange(stream.ToArray());
        }

        public static void Write(Stream destination, PythonCodeObject codeObject, PycOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(codeObject);
            if (!destination.CanWrite)
                throw new ArgumentException("The destination stream is not writable.", nameof(destination));
            if (codeObject.Version != PythonBytecodeVersion.CPython3_14_6)
                throw new NotSupportedException($"Unsupported bytecode version {codeObject.Version}.");

            options ??= new PycOptions();

            // CPython 3.14.6: PYC_MAGIC_NUMBER 3627 followed by CR LF
            WriteUInt16(destination, CPython3146OpcodeProfile.MagicNumber);
            destination.WriteByte(0x0D);
            destination.WriteByte(0x0A);

            // PEP 552 flags = 0
            WriteUInt32(destination, 0);
            WriteUInt32(destination, options.SourceTimestamp);
            WriteUInt32(destination, options.SourceSize);

            var marshal = new CPython3146MarshalWriter(destination);
            marshal.WriteCodeObject(codeObject);
        }

        public static void WriteFile(
            string path,
            PythonCodeObject codeObject,
            PycOptions? options = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            Write(stream, codeObject, options);
        }

        private static void WriteUInt16(Stream stream, int value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }
    }

    internal sealed class CPython3146MarshalWriter
    {
        private const byte TypeNone = (byte)'N';
        private const byte TypeFalse = (byte)'F';
        private const byte TypeTrue = (byte)'T';
        private const byte TypeEllipsis = (byte)'.';
        private const byte TypeInt = (byte)'i';
        private const byte TypeLong = (byte)'l';
        private const byte TypeBinaryFloat = (byte)'g';
        private const byte TypeBinaryComplex = (byte)'y';
        private const byte TypeBytes = (byte)'s';
        private const byte TypeTuple = (byte)'(';
        private const byte TypeSmallTuple = (byte)')';
        private const byte TypeUnicode = (byte)'u';
        private const byte TypeFrozenSet = (byte)'>';
        private const byte TypeCode = (byte)'c';

        private readonly Stream _stream;

        public CPython3146MarshalWriter(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        public void WriteCodeObject(PythonCodeObject codeObject)
        {
            _stream.WriteByte(TypeCode);
            WriteInt32(codeObject.ArgumentCount);
            WriteInt32(codeObject.PositionalOnlyArgumentCount);
            WriteInt32(codeObject.KeywordOnlyArgumentCount);
            WriteInt32(codeObject.StackSize);
            WriteInt32((int)codeObject.Flags);
            WriteBytes(codeObject.Bytecode.AsSpan());
            WriteConstants(codeObject.Constants);
            WriteStrings(codeObject.Names);
            WriteStrings(codeObject.LocalsPlusNames);

            var localKinds = new byte[codeObject.LocalsPlusKinds.Length];
            for (var i = 0; i < localKinds.Length; i++)
                localKinds[i] = (byte)codeObject.LocalsPlusKinds[i];
            WriteBytes(localKinds);

            WriteUnicode(codeObject.FileName);
            WriteUnicode(codeObject.Name);
            WriteUnicode(codeObject.QualifiedName);
            WriteInt32(codeObject.FirstLineNumber);
            WriteBytes(codeObject.LineTable.AsSpan());
            WriteBytes(codeObject.ExceptionTable.AsSpan());
        }

        private void WriteConstant(PythonConstant constant)
        {
            switch (constant)
            {
                case PythonNoneConstant:
                    _stream.WriteByte(TypeNone);
                    return;

                case EllipsisConstant:
                    _stream.WriteByte(TypeEllipsis);
                    return;

                case BooleanConstant boolean:
                    _stream.WriteByte(boolean.Value ? TypeTrue : TypeFalse);
                    return;

                case IntegerConstant integer:
                    WriteInteger(integer.Value);
                    return;

                case FloatConstant floating:
                    _stream.WriteByte(TypeBinaryFloat);
                    WriteDouble(floating.Value);
                    return;

                case ComplexConstant complex:
                    _stream.WriteByte(TypeBinaryComplex);
                    WriteDouble(complex.Real);
                    WriteDouble(complex.Imaginary);
                    return;

                case StringConstant text:
                    WriteUnicode(text.Value);
                    return;

                case BytesConstant bytes:
                    WriteBytes(bytes.Value.AsSpan());
                    return;

                case TupleConstant tuple:
                    WriteTupleHeader(tuple.Items.Length);
                    foreach (var item in tuple.Items)
                        WriteConstant(item);
                    return;

                case FrozenSetConstant frozenSet:
                    _stream.WriteByte(TypeFrozenSet);
                    WriteInt32(frozenSet.Items.Length);
                    foreach (var item in frozenSet.Items)
                        WriteConstant(item);
                    return;

                case CodeConstant code:
                    WriteCodeObject(code.Value);
                    return;

                default:
                    throw new NotSupportedException($"Unsupported marshal constant {constant.GetType().Name}.");
            }
        }

        private void WriteConstants(ImmutableArray<PythonConstant> constants)
        {
            WriteTupleHeader(constants.Length);
            foreach (var constant in constants)
                WriteConstant(constant);
        }

        private void WriteStrings(ImmutableArray<string> strings)
        {
            WriteTupleHeader(strings.Length);
            foreach (var value in strings)
                WriteUnicode(value);
        }

        private void WriteTupleHeader(int length)
        {
            if ((uint)length <= byte.MaxValue)
            {
                _stream.WriteByte(TypeSmallTuple);
                _stream.WriteByte((byte)length);
            }
            else
            {
                _stream.WriteByte(TypeTuple);
                WriteInt32(length);
            }
        }

        private void WriteInteger(BigInteger value)
        {
            if (value >= int.MinValue && value <= int.MaxValue)
            {
                _stream.WriteByte(TypeInt);
                WriteInt32((int)value);
                return;
            }

            _stream.WriteByte(TypeLong);
            var negative = value.Sign < 0;
            var magnitude = BigInteger.Abs(value);
            var digits = 0;
            for (var remaining = magnitude; remaining != BigInteger.Zero; remaining >>= 15)
                digits++;

            WriteInt32(negative ? -digits : digits);
            for (var i = 0; i < digits; i++)
            {
                WriteUInt16((ushort)(magnitude & 0x7FFF));
                magnitude >>= 15;
            }
        }

        private void WriteUnicode(string value)
        {
            _stream.WriteByte(TypeUnicode);
            var bytes = EncodeUtf8SurrogatePass(value);
            WriteInt32(bytes.Length);
            _stream.Write(bytes);
        }

        private static byte[] EncodeUtf8SurrogatePass(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            using var stream = new MemoryStream(checked(value.Length * 3));
            for (var i = 0; i < value.Length; i++)
            {
                int codePoint = value[i];
                if (char.IsHighSurrogate(value[i]) &&
                    i + 1 < value.Length &&
                    char.IsLowSurrogate(value[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(value[i], value[++i]);
                }

                if (codePoint <= 0x7F)
                {
                    stream.WriteByte((byte)codePoint);
                }
                else if (codePoint <= 0x7FF)
                {
                    stream.WriteByte((byte)(0xC0 | (codePoint >> 6)));
                    stream.WriteByte((byte)(0x80 | (codePoint & 0x3F)));
                }
                else if (codePoint <= 0xFFFF)
                {
                    stream.WriteByte((byte)(0xE0 | (codePoint >> 12)));
                    stream.WriteByte((byte)(0x80 | ((codePoint >> 6) & 0x3F)));
                    stream.WriteByte((byte)(0x80 | (codePoint & 0x3F)));
                }
                else
                {
                    stream.WriteByte((byte)(0xF0 | (codePoint >> 18)));
                    stream.WriteByte((byte)(0x80 | ((codePoint >> 12) & 0x3F)));
                    stream.WriteByte((byte)(0x80 | ((codePoint >> 6) & 0x3F)));
                    stream.WriteByte((byte)(0x80 | (codePoint & 0x3F)));
                }
            }
            return stream.ToArray();
        }

        private void WriteBytes(ReadOnlySpan<byte> value)
        {
            _stream.WriteByte(TypeBytes);
            WriteInt32(value.Length);
            _stream.Write(value);
        }

        private void WriteDouble(double value)
        {
            var bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
            for (var shift = 0; shift < 64; shift += 8)
                _stream.WriteByte((byte)(bits >> shift));
        }

        private void WriteUInt16(ushort value)
        {
            _stream.WriteByte((byte)value);
            _stream.WriteByte((byte)(value >> 8));
        }

        private void WriteInt32(int value)
        {
            var unsigned = unchecked((uint)value);
            _stream.WriteByte((byte)unsigned);
            _stream.WriteByte((byte)(unsigned >> 8));
            _stream.WriteByte((byte)(unsigned >> 16));
            _stream.WriteByte((byte)(unsigned >> 24));
        }
    }
}
