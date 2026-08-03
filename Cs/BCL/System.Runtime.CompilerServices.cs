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
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class InlineArrayAttribute : Attribute
    {
        public InlineArrayAttribute(int length)
        {
            Length = length;
        }

        public int Length { get; }
    }
    [InlineArray(2)]
    public struct InlineArray2<T>
    {
        private T _element0;
    }
    [InlineArray(3)]
    public struct InlineArray3<T>
    {
        private T _element0;
    }
    [InlineArray(4)]
    public struct InlineArray4<T>
    {
        private T _element0;
    }
    [InlineArray(5)]
    public struct InlineArray5<T>
    {
        private T _element0;
    }
    [InlineArray(6)]
    public struct InlineArray6<T>
    {
        private T _element0;
    }
    [InlineArray(7)]
    public struct InlineArray7<T>
    {
        private T _element0;
    }
    [InlineArray(8)]
    public struct InlineArray8<T>
    {
        private T _element0;
    }
    [InlineArray(9)]
    public struct InlineArray9<T>
    {
        private T _element0;
    }
    [InlineArray(10)]
    public struct InlineArray10<T>
    {
        private T _element0;
    }
    [InlineArray(11)]
    public struct InlineArray11<T>
    {
        private T _element0;
    }
    [InlineArray(12)]
    public struct InlineArray12<T>
    {
        private T _element0;
    }
    [InlineArray(13)]
    public struct InlineArray13<T>
    {
        private T _element0;
    }
    [InlineArray(14)]
    public struct InlineArray14<T>
    {
        private T _element0;
    }
    [InlineArray(15)]
    public struct InlineArray15<T>
    {
        private T _element0;
    }
    [InlineArray(16)]
    public struct InlineArray16<T>
    {
        private T _element0;
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T As<T>(object o) where T : class?
        {
            throw new PlatformNotSupportedException();
            // ldarg.0
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref TTo As<TFrom, TTo>(ref TFrom source)
            where TFrom : allows ref struct
            where TTo : allows ref struct
        {
            throw new PlatformNotSupportedException();
            // ldarg.0
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ref T AsRef<T>(void* source)
            where T : allows ref struct
        {
            return ref *(T*)source;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T AsRef<T>(scoped ref readonly T source)
            where T : allows ref struct
        {
            //ldarg .0
            //ret
            return ref source;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint ByteOffset<T>(ref T origin, ref T target)
            where T : allows ref struct
        {
            return 0;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOf<T>()
            where T : allows ref struct
        {
            return 0;
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullRef<T>(ref readonly T source)
            where T : allows ref struct
        {
            return true;
            // ldarg.0
            // ldc.i4.0
            // conv.u
            // ceq
            // ret
        }
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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