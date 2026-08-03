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