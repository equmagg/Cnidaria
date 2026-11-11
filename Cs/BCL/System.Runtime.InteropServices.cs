using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Threading;

namespace System.Runtime.InteropServices
{
    public static class Marshal
    {
        public static IntPtr AllocHGlobal(int cb) => AllocHGlobal((nint)cb);
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static unsafe IntPtr AllocHGlobal(nint cb) => IntPtr.Zero;
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static unsafe void FreeHGlobal(IntPtr hglobal) { }

        public static int GetLastPInvokeError()
        {
            return PInvokeMarshal.t_lastError;
        }

        public static void SetLastPInvokeError(int error)
        {
            PInvokeMarshal.t_lastError = error;
        }

        internal static unsafe bool IsPinnable(object o)
        {
            return (o == null) || !o.GetMethodTable()->ContainsGCPointers;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static unsafe void LinuxRequestShutdown() { throw new PlatformNotSupportedException(); }
    }
    internal static class PInvokeMarshal
    {
        [ThreadStatic]
        internal static int t_lastError;
    }
    public static unsafe class MemoryMarshal
    {
        /// <summary>
        /// Creates a new span over a portion of a regular managed object. This can be useful
        /// if part of a managed object represents a "fixed array." This is dangerous because the
        /// <paramref name="length"/> is not checked.
        /// </summary>
        /// <param name="reference">A reference to data.</param>
        /// <param name="length">The number of <typeparamref name="T"/> elements the memory contains.</param>
        /// <returns>A span representing the specified reference and length.</returns>
        /// <remarks>
        /// This method should be used with caution. It is dangerous because the length argument is not checked.
        /// Even though the ref is annotated as scoped, it will be stored into the returned span, and the lifetime
        /// of the returned span will not be validated for safety, even by span-aware languages.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<T> CreateSpan<T>(scoped ref T reference, int length) =>
            new Span<T>(ref Unsafe.AsRef(in reference), length);

        /// <summary>
        /// Creates a new read-only span over a portion of a regular managed object. This can be useful
        /// if part of a managed object represents a "fixed array." This is dangerous because the
        /// <paramref name="length"/> is not checked.
        /// </summary>
        /// <param name="reference">A reference to data.</param>
        /// <param name="length">The number of <typeparamref name="T"/> elements the memory contains.</param>
        /// <returns>A read-only span representing the specified reference and length.</returns>
        /// <remarks>
        /// This method should be used with caution. It is dangerous because the length argument is not checked.
        /// Even though the ref is annotated as scoped, it will be stored into the returned span, and the lifetime
        /// of the returned span will not be validated for safety, even by span-aware languages.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<T> CreateReadOnlySpan<T>(scoped ref readonly T reference, int length) =>
            new ReadOnlySpan<T>(ref Unsafe.AsRef(in reference), length);

        [Intrinsic]
        public static ref T GetArrayDataReference<T>(T[] array)
        {
            throw new NotSupportedException();
        }

        public static unsafe ref byte GetArrayDataReference(Array array)
            => ref Unsafe.AddByteOffset(ref Unsafe.As<RawData>(array).Data, (nuint)RuntimeHelpers.GetMethodTable(array)->BaseSize - (nuint)(2 * sizeof(IntPtr)));

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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Span<byte> AsBytes<T>(Span<T> span)
            where T : struct
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                throw new NotSupportedException();

            return new Span<byte>(
                ref Unsafe.As<T, byte>(ref GetReference(span)),
                checked(span.Length * sizeof(T)));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ReadOnlySpan<byte> AsBytes<T>(ReadOnlySpan<T> span)
            where T : struct
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                throw new NotSupportedException();

            return new ReadOnlySpan<byte>(
                ref Unsafe.As<T, byte>(ref GetReference(span)),
                checked(span.Length * sizeof(T)));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Span<TTo> Cast<TFrom, TTo>(Span<TFrom> span)
            where TFrom : struct
            where TTo : struct
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<TFrom>())
                throw new InvalidCastException();
            if (RuntimeHelpers.IsReferenceOrContainsReferences<TTo>())
                throw new InvalidCastException();

            // Use unsigned integers - unsigned division by constant (especially by power of 2)
            // and checked casts are faster and smaller.
            uint fromSize = (uint)sizeof(TFrom);
            uint toSize = (uint)sizeof(TTo);
            uint fromLength = (uint)span.Length;
            int toLength;
            if (fromSize == toSize)
            {
                // Special case for same size types - `(ulong)fromLength * (ulong)fromSize / (ulong)toSize`
                // should be optimized to just `length` but the JIT doesn't do that today.
                toLength = (int)fromLength;
            }
            else if (fromSize == 1)
            {
                // Special case for byte sized TFrom - `(ulong)fromLength * (ulong)fromSize / (ulong)toSize`
                // becomes `(ulong)fromLength / (ulong)toSize` but the JIT can't narrow it down to `int`
                // and can't eliminate the checked cast. This also avoids a 32 bit specific issue,
                // the JIT can't eliminate long multiply by 1.
                toLength = (int)(fromLength / toSize);
            }
            else
            {
                // Ensure that casts are done in such a way that the JIT is able to "see"
                // the uint->ulong casts and the multiply together so that on 32 bit targets
                // 32x32to64 multiplication is used.
                ulong toLengthUInt64 = (ulong)fromLength * (ulong)fromSize / (ulong)toSize;
                toLength = checked((int)toLengthUInt64);
            }

            return new Span<TTo>(
                ref Unsafe.As<TFrom, TTo>(ref span._reference),
                toLength);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ReadOnlySpan<TTo> Cast<TFrom, TTo>(ReadOnlySpan<TFrom> span)
            where TFrom : struct
            where TTo : struct
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<TFrom>())
                throw new InvalidCastException();
            if (RuntimeHelpers.IsReferenceOrContainsReferences<TTo>())
                throw new InvalidCastException();

            // Use unsigned integers - unsigned division by constant (especially by power of 2)
            // and checked casts are faster and smaller.
            uint fromSize = (uint)sizeof(TFrom);
            uint toSize = (uint)sizeof(TTo);
            uint fromLength = (uint)span.Length;
            int toLength;
            if (fromSize == toSize)
            {
                // Special case for same size types - `(ulong)fromLength * (ulong)fromSize / (ulong)toSize`
                // should be optimized to just `length` but the JIT doesn't do that today.
                toLength = (int)fromLength;
            }
            else if (fromSize == 1)
            {
                // Special case for byte sized TFrom - `(ulong)fromLength * (ulong)fromSize / (ulong)toSize`
                // becomes `(ulong)fromLength / (ulong)toSize` but the JIT can't narrow it down to `int`
                // and can't eliminate the checked cast. This also avoids a 32 bit specific issue,
                // the JIT can't eliminate long multiply by 1.
                toLength = (int)(fromLength / toSize);
            }
            else
            {
                // Ensure that casts are done in such a way that the JIT is able to "see"
                // the uint->ulong casts and the multiply together so that on 32 bit targets
                // 32x32to64 multiplication is used.
                ulong toLengthUInt64 = (ulong)fromLength * (ulong)fromSize / (ulong)toSize;
                toLength = checked((int)toLengthUInt64);
            }

            return new ReadOnlySpan<TTo>(
                ref Unsafe.As<TFrom, TTo>(ref GetReference(span)),
                toLength);
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
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class UnmanagedCallersOnlyAttribute : Attribute
    {
        public UnmanagedCallersOnlyAttribute()
        {
        }

        public string? EntryPoint;
    }
    /// <summary>
    /// An attribute used to indicate a GC transition should be skipped when making an unmanaged function call.
    /// </summary>
    /// <example>
    /// Example of a valid use case. The Win32 `GetTickCount()` function is a small performance related function
    /// that reads some global memory and returns the value. In this case, the GC transition overhead is significantly
    /// more than the memory read.
    /// <code>
    /// using System;
    /// using System.Runtime.InteropServices;
    /// class Program
    /// {
    ///     [DllImport("Kernel32")]
    ///     [SuppressGCTransition]
    ///     static extern int GetTickCount();
    ///     static void Main()
    ///     {
    ///         Console.WriteLine($"{GetTickCount()}");
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// This attribute is ignored if applied to a method without the <see cref="DllImportAttribute"/>.
    /// 
    /// The runtime may load the native library for method marked with this attribute in advance before the method is called for the first time.
    /// Usage of this attribute is not recommended for platform neutral libraries with conditional platform specific code.
    /// 
    /// The P/Invoke method that this attribute is applied to must have all of the following properties:
    ///   * Native function always executes for a trivial amount of time (less than 1 microsecond).
    ///   * Native function does not perform a blocking syscall (e.g. any type of I/O).
    ///   * Native function does not call back into the runtime (e.g. Reverse P/Invoke).
    ///   * Native function does not throw exceptions.
    ///   * Native function does not manipulate locks or other concurrency primitives.
    ///
    /// Consequences of invalid uses of this attribute:
    ///   * GC starvation.
    ///   * Immediate runtime termination.
    ///   * Data corruption.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class SuppressGCTransitionAttribute : Attribute
    {
        public SuppressGCTransitionAttribute()
        {
        }
    }
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class DllImportAttribute : Attribute
    {
        public DllImportAttribute(string dllName)
        {
            Value = dllName;
        }

        public string Value { get; }

        public string? EntryPoint;
        public CharSet CharSet = CharSet.None;
        public bool SetLastError;
        public bool ExactSpelling;
        public CallingConvention CallingConvention = CallingConvention.Winapi;
        public bool BestFitMapping;
        public bool PreserveSig = true;
        public bool ThrowOnUnmappableChar;
    }
    /// <summary>
    /// Attribute used to indicate a source generator should create a function for marshalling
    /// arguments instead of relying on the runtime to generate an equivalent marshalling function at run-time.
    /// </summary>
    /// <remarks>
    /// This attribute is meaningless if the source generator associated with it is not enabled.
    /// The current built-in source generator only supports C# and only supplies an implementation when
    /// applied to static, partial, non-generic methods.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class LibraryImportAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryImportAttribute"/>.
        /// </summary>
        /// <param name="libraryName">Name of the library containing the import.</param>
        public LibraryImportAttribute(string libraryName)
        {
            LibraryName = libraryName;
        }

        /// <summary>
        /// Gets the name of the library containing the import.
        /// </summary>
        public string LibraryName { get; }

        /// <summary>
        /// Gets or sets the name of the entry point to be called.
        /// </summary>
        public string? EntryPoint { get; set; }

        /// <summary>
        /// Gets or sets how to marshal string arguments to the method.
        /// </summary>
        /// <remarks>
        /// If this field is set to a value other than <see cref="StringMarshalling.Custom" />,
        /// <see cref="StringMarshallingCustomType" /> must not be specified.
        /// </remarks>
        public StringMarshalling StringMarshalling { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="Type"/> used to control how string arguments to the method are marshalled.
        /// </summary>
        /// <remarks>
        /// If this field is specified, <see cref="StringMarshalling" /> must not be specified
        /// or must be set to <see cref="StringMarshalling.Custom" />.
        /// </remarks>
        public Type? StringMarshallingCustomType { get; set; }

        /// <summary>
        /// Gets or sets whether the callee sets an error (SetLastError on Windows or errno
        /// on other platforms) before returning from the attributed method.
        /// </summary>
        public bool SetLastError { get; set; }
    }
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.ReturnValue, Inherited = false)]
    public sealed partial class MarshalAsAttribute : Attribute
    {
        public MarshalAsAttribute(UnmanagedType unmanagedType)
        {
            Value = unmanagedType;
        }
        public MarshalAsAttribute(short unmanagedType)
        {
            Value = (UnmanagedType)unmanagedType;
        }

        public UnmanagedType Value { get; }

        // Fields used with SubType = SafeArray.
        public VarEnum SafeArraySubType;
        public Type? SafeArrayUserDefinedSubType;

        // Field used with iid_is attribute (interface pointers).
        public int IidParameterIndex;

        // Fields used with SubType = ByValArray and LPArray.
        // Array size =  parameter(PI) * PM + C
        public UnmanagedType ArraySubType;
        public short SizeParamIndex;           // param index PI
        public int SizeConst;                // constant C

        // Fields used with SubType = CustomMarshaler
        public string? MarshalType;              // Name of marshaler class
        public Type? MarshalTypeRef;           // Type of marshaler class
        public string? MarshalCookie;            // cookie to pass to marshaler
    }
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class InAttribute : Attribute
    {
        public InAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class OutAttribute : Attribute
    {
        public OutAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public sealed class StructLayoutAttribute : Attribute
    {
        public StructLayoutAttribute(LayoutKind layoutKind)
        {
            Value = layoutKind;
        }

        public StructLayoutAttribute(short layoutKind)
        {
            Value = (LayoutKind)layoutKind;
        }

        public LayoutKind Value { get; }

        public int Pack;
        public int Size;
        public CharSet CharSet;
    }
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class FieldOffsetAttribute : Attribute
    {
        public FieldOffsetAttribute(int offset)
        {
            Value = offset;
        }

        public int Value { get; }
    }
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Interface | AttributeTargets.Class | AttributeTargets.Struct | 
        AttributeTargets.Delegate | AttributeTargets.Enum | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
    public sealed class ComVisibleAttribute : Attribute
    {
        public ComVisibleAttribute(bool visibility)
        {
            Value = visibility;
        }

        public bool Value { get; }
    }
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, Inherited = false)]
    public sealed class ClassInterfaceAttribute : Attribute
    {
        public ClassInterfaceAttribute(ClassInterfaceType classInterfaceType)
        {
            Value = classInterfaceType;
        }
        public ClassInterfaceAttribute(short classInterfaceType)
        {
            Value = (ClassInterfaceType)classInterfaceType;
        }

        public ClassInterfaceType Value { get; }
    }
    public enum VarEnum
    {
        VT_EMPTY = 0,
        VT_NULL = 1,
        VT_I2 = 2,
        VT_I4 = 3,
        VT_R4 = 4,
        VT_R8 = 5,
        VT_CY = 6,
        VT_DATE = 7,
        VT_BSTR = 8,
        VT_DISPATCH = 9,
        VT_ERROR = 10,
        VT_BOOL = 11,
        VT_VARIANT = 12,
        VT_UNKNOWN = 13,
        VT_DECIMAL = 14,
        VT_I1 = 16,
        VT_UI1 = 17,
        VT_UI2 = 18,
        VT_UI4 = 19,
        VT_I8 = 20,
        VT_UI8 = 21,
        VT_INT = 22,
        VT_UINT = 23,
        VT_VOID = 24,
        VT_HRESULT = 25,
        VT_PTR = 26,
        VT_SAFEARRAY = 27,
        VT_CARRAY = 28,
        VT_USERDEFINED = 29,
        VT_LPSTR = 30,
        VT_LPWSTR = 31,
        VT_RECORD = 36,
        VT_FILETIME = 64,
        VT_BLOB = 65,
        VT_STREAM = 66,
        VT_STORAGE = 67,
        VT_STREAMED_OBJECT = 68,
        VT_STORED_OBJECT = 69,
        VT_BLOB_OBJECT = 70,
        VT_CF = 71,
        VT_CLSID = 72,
        VT_VECTOR = 0x1000,
        VT_ARRAY = 0x2000,
        VT_BYREF = 0x4000
    }
    public enum UnmanagedType
    {
        Bool = 0x2,         // 4 byte boolean value (true != 0, false == 0)
        I1 = 0x3,           // 1 byte signed value
        U1 = 0x4,           // 1 byte unsigned value
        I2 = 0x5,           // 2 byte signed value
        U2 = 0x6,           // 2 byte unsigned value
        I4 = 0x7,           // 4 byte signed value
        U4 = 0x8,           // 4 byte unsigned value
        I8 = 0x9,           // 8 byte signed value
        U8 = 0xa,           // 8 byte unsigned value
        R4 = 0xb,           // 4 byte floating point
        R8 = 0xc,           // 8 byte floating point
        Currency = 0xf,     // A currency
        BStr = 0x13,        // OLE Unicode BSTR
        LPStr = 0x14,       // Ptr to SBCS string
        LPWStr = 0x15,      // Ptr to Unicode string
        LPTStr = 0x16,      // Ptr to OS preferred (SBCS/Unicode) string
        ByValTStr = 0x17,   // OS preferred (SBCS/Unicode) inline string (only valid in structs)
        IUnknown = 0x19,    // COM IUnknown pointer.
        IDispatch = 0x1a,   // COM IDispatch pointer
        Struct = 0x1b,      // Structure
        Interface = 0x1c,   // COM interface
        SafeArray = 0x1d,   // OLE SafeArray
        ByValArray = 0x1e,  // Array of fixed size (only valid in structs)
        SysInt = 0x1f,      // Hardware natural sized signed integer
        SysUInt = 0x20,
        [Obsolete("Marshalling as VBByRefString may be unavailable in future releases.")]
        VBByRefStr = 0x22,
        [Obsolete("Marshalling as AnsiBStr may be unavailable in future releases.")]
        AnsiBStr = 0x23,    // OLE BSTR containing SBCS characters
        [Obsolete("Marshalling as TBstr may be unavailable in future releases.")]
        TBStr = 0x24,       // Ptr to OS preferred (SBCS/Unicode) BSTR
        VariantBool = 0x25, // OLE defined BOOLEAN (2 bytes, true == -1, false == 0)
        FunctionPtr = 0x26, // Function pointer
        [Obsolete("Marshalling arbitrary types may be unavailable in future releases. Specify the type you wish to marshal as.")]
        AsAny = 0x28,       // Paired with Object type and does runtime marshalling determination
        LPArray = 0x2a,     // C style array
        LPStruct = 0x2b,    // Pointer to a structure
        CustomMarshaler = 0x2c,
        Error = 0x2d,
        IInspectable = 0x2e,
        HString = 0x2f,     // Windows Runtime HSTRING
        LPUTF8Str = 0x30,   // UTF8 string
    }
    /// <summary>
    /// Specifies how strings should be marshalled for generated p/invokes
    /// </summary>
    public enum StringMarshalling
    {
        /// <summary>
        /// Indicates the user is supplying a specific marshaller in <see cref="LibraryImportAttribute.StringMarshallingCustomType"/>.
        /// </summary>
        Custom = 0,
        /// <summary>
        /// Use the platform-provided UTF-8 marshaller.
        /// </summary>
        Utf8,
        /// <summary>
        /// Use the platform-provided UTF-16 marshaller.
        /// </summary>
        Utf16,
    }
    public enum ClassInterfaceType
    {
        None = 0,
        AutoDispatch = 1,
        AutoDual = 2
    }
    public enum CallingConvention
    {
        Winapi = 1,
        Cdecl = 2,
        StdCall = 3,
        ThisCall = 4,
        FastCall = 5,
    }
    public enum CharSet
    {
        None = 1,        // User didn't specify how to marshal strings.
        Ansi = 2,        // Strings should be marshalled as ANSI 1 byte chars.
        Unicode = 3,     // Strings should be marshalled as Unicode 2 byte chars.
        Auto = 4,        // Marshal Strings in the right way for the target system.
    }
    public enum LayoutKind
    {
        Sequential = 0,
        Explicit = 2,
        Auto = 3,
    }

    public enum GCHandleType
    {
        Weak = 0,
        WeakTrackResurrection = 1,
        Normal = 2,
        Pinned = 3  // NOTE: not implemented here
    }

    /// <summary>
    /// Represents an opaque, GC handle to a managed object. A GC handle is used when an
    /// object reference must be reachable from unmanaged memory.
    /// </summary>
    /// <remarks>
    /// There are 4 kinds of roots:
    /// Normal: Keeps the object from being collected.
    /// Weak: Allows object to be collected and handle contents will be zeroed.
    /// Weak references are zeroed before the finalizer runs, so if the
    /// object is resurrected in the finalizer the weak reference is still zeroed.
    /// WeakTrackResurrection: Same as Weak, but stays until after object is really gone.
    /// Pinned - same as Normal, but allows the address of the actual object to be taken.
    /// </remarks>
    /// <seealso cref="GCHandle{T}"/>
    /// <seealso cref="PinnedGCHandle{T}"/>
    /// <seealso cref="WeakGCHandle{T}"/>
    [StructLayout(LayoutKind.Sequential)]
    public struct GCHandle : IEquatable<GCHandle>
    {
        // The actual integer handle value that the EE uses internally.
        private IntPtr _handle;

        // Allocate a handle storing the object and the type.
        private GCHandle(object? value, GCHandleType type)
        {
            // Make sure the type parameter is within the valid range for the enum.
            if ((uint)type > (uint)GCHandleType.Pinned) // IMPORTANT: This must be kept in sync with the GCHandleType enum.
            {
                throw new ArgumentOutOfRangeException(nameof(type), SR.ArgumentOutOfRange_Enum);
            }

            if (type == GCHandleType.Pinned && !Marshal.IsPinnable(value))
            {
                throw new ArgumentException(SR.ArgumentException_NotIsomorphic, nameof(value));
            }

            IntPtr handle = InternalAlloc(value, type);

            if (type == GCHandleType.Pinned)
            {
                // Record if the handle is pinned.
                handle |= 1;
            }

            _handle = handle;
        }

        // Used in the conversion functions below.
        private GCHandle(IntPtr handle) => _handle = handle;

        /// <summary>Creates a new GC handle for an object.</summary>
        /// <param name="value">The object that the GC handle is created for.</param>
        /// <returns>A new GC handle that protects the object.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GCHandle Alloc(object? value) => new GCHandle(value, GCHandleType.Normal);

        /// <summary>Creates a new GC handle for an object.</summary>
        /// <param name="value">The object that the GC handle is created for.</param>
        /// <param name="type">The type of GC handle to create.</param>
        /// <returns>A new GC handle that protects the object.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GCHandle Alloc(object? value, GCHandleType type) => new GCHandle(value, type);

        /// <summary>Frees a GC handle.</summary>
        public void Free()
        {
            // Free the handle if it hasn't already been freed.
            IntPtr handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            ThrowIfInvalid(handle);
            InternalFree(GetHandleValue(handle));
        }

        // Target property - allows getting / updating of the handle's referent.
        public object? Target
        {
            readonly get
            {
                IntPtr handle = _handle;
                ThrowIfInvalid(handle);

                return InternalGet(GetHandleValue(handle));
            }
            set
            {
                IntPtr handle = _handle;
                ThrowIfInvalid(handle);

                if (IsPinned(handle) && !Marshal.IsPinnable(value))
                {
                    throw new ArgumentException(SR.ArgumentException_NotIsomorphic, nameof(value));
                }

                InternalSet(GetHandleValue(handle), value);
            }
        }

        /// <summary>
        /// Retrieve the address of an object in a Pinned handle.  This throws
        /// an exception if the handle is any type other than Pinned.
        /// </summary>
        public readonly IntPtr AddrOfPinnedObject()
        {
            // Check if the handle was not a pinned handle.
            // You can only get the address of pinned handles.
            IntPtr handle = _handle;
            ThrowIfInvalid(handle);

            if (!IsPinned(handle))
            {
                ThrowHelper.ThrowInvalidOperationException_HandleIsNotPinned();
            }

            // Get the address.

            object? target = InternalGet(GetHandleValue(handle));
            if (target is null)
            {
                return default;
            }

            unsafe
            {
                // Unsafe.AsPointer calls are safe since object is pinned.
                if (RuntimeHelpers.ObjectHasComponentSize(target))
                {
                    if (target.GetType() == typeof(string))
                    {
                        return (IntPtr)Unsafe.AsPointer(ref Unsafe.As<string>(target).GetRawStringData());
                    }

                    Debug.Assert(target is Array);
                    return (IntPtr)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(Unsafe.As<Array>(target)));
                }

                return (IntPtr)Unsafe.AsPointer(ref target.GetRawData());
            }
        }

        /// <summary>Determine whether this handle has been allocated or not.</summary>
        public readonly bool IsAllocated => _handle != 0;

        /// <summary>
        /// Used to create a GCHandle from an int.  This is intended to
        /// be used with the reverse conversion.
        /// </summary>
        public static explicit operator GCHandle(IntPtr value) => FromIntPtr(value);

        public static GCHandle FromIntPtr(IntPtr value)
        {
            ThrowIfInvalid(value);
            return new GCHandle(value);
        }

        /// <summary>Used to get the internal integer representation of the handle out.</summary>
        public static explicit operator IntPtr(GCHandle value) => ToIntPtr(value);

        public static IntPtr ToIntPtr(GCHandle value) => value._handle;

        public override readonly int GetHashCode() => _handle.GetHashCode();

        public override readonly bool Equals([NotNullWhen(true)] object? o) => o is GCHandle other && Equals(other);

        /// <summary>Indicates whether the current instance is equal to another instance of the same type.</summary>
        /// <param name="other">An instance to compare with this instance.</param>
        /// <returns>true if the current instance is equal to the other instance; otherwise, false.</returns>
        public readonly bool Equals(GCHandle other) => _handle == other._handle;

        public static bool operator ==(GCHandle a, GCHandle b) => a._handle == b._handle;

        public static bool operator !=(GCHandle a, GCHandle b) => a._handle != b._handle;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IntPtr GetHandleValue(IntPtr handle) => new IntPtr(handle & ~1); // Remove Pin flag

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsPinned(IntPtr handle) => (handle & 1) != 0; // Check Pin flag

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ThrowIfInvalid(IntPtr handle)
        {
            // Check if the handle was never initialized or was freed.
            if (handle == 0)
            {
                ThrowHelper.ThrowInvalidOperationException_HandleIsNotInitialized();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void CheckUninitialized(IntPtr handle)
        {
            // Check if the handle was never initialized or was freed.
            // Throws NRE with minimal overhead, to avoid access violation from unmanaged code.
            // Invalid handle is unsupported and will cause AV as expected.
#if MONO
            // Mono doesn't handle reading null pointer as NRE.
            // Throw a NRE manually.
            if (handle == 0)
            {
                throw new NullReferenceException();
            }
#else
            // The read will be combined with the read in InternalGet under Release.
            _ = *(object*)handle;
#endif
        }

        internal static IntPtr InternalAlloc(object value, GCHandleType type) => RuntimeImports.RhHandleAlloc(value, type);

        internal static void InternalFree(IntPtr handle) => RuntimeImports.RhHandleFree(handle);

        internal static object? InternalGet(IntPtr handle) => RuntimeImports.RhHandleGet(handle);

        internal static void InternalSet(IntPtr handle, object? value) => RuntimeImports.RhHandleSet(handle, value);
    }

    // This implementation does not employ critical execution regions and thus cannot
    // reliably guarantee handle release in the face of thread aborts.

    /// <summary>Represents a wrapper class for operating system handles.</summary>
    public abstract partial class SafeHandle : CriticalFinalizerObject, IDisposable
    {
        /// <summary>Specifies the handle to be wrapped.</summary>
        protected IntPtr handle;
        /// <summary>Combined ref count and closed/disposed flags (so we can atomically modify them).</summary>
        private volatile int _state;
        /// <summary>Whether we can release this handle.</summary>
        private readonly bool _ownsHandle;
        /// <summary>Whether constructor completed.</summary>
        private readonly bool _fullyInitialized;

        /// <summary>Bitmasks for the <see cref="_state"/> field.</summary>
        /// <remarks>
        /// The state field ends up looking like this:
        ///
        ///  31                                                        2  1   0
        /// +-----------------------------------------------------------+---+---+
        /// |                           Ref count                       | D | C |
        /// +-----------------------------------------------------------+---+---+
        ///
        /// Where D = 1 means a Dispose has been performed and C = 1 means the
        /// underlying handle has been (or will be shortly) released.
        /// </remarks>
        private static class StateBits
        {
            public const int Closed = 0b01;
            public const int Disposed = 0b10;
            public const int RefCount = unchecked(~0b11); // 2 bits reserved for closed/disposed; ref count gets 30 bits
            public const int RefCountOne = 1 << 2;
        }

        /// <summary>Creates a SafeHandle class.</summary>
        protected SafeHandle(IntPtr invalidHandleValue, bool ownsHandle)
        {
            handle = invalidHandleValue;
            _state = StateBits.RefCountOne; // Ref count 1 and not closed or disposed.
            _ownsHandle = ownsHandle;

            if (!ownsHandle)
            {
                GC.SuppressFinalize(this);
            }

            Volatile.Write(ref _fullyInitialized, true);
        }

        ~SafeHandle()
        {
            if (_fullyInitialized)
            {
                Dispose(disposing: false);
            }
        }

        internal bool OwnsHandle => _ownsHandle;

        protected internal void SetHandle(IntPtr handle) => this.handle = handle;

        public IntPtr DangerousGetHandle() => handle;

        public bool IsClosed => (_state & StateBits.Closed) == StateBits.Closed;

        public abstract bool IsInvalid { get; }

        public void Close() => Dispose();

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            InternalRelease(disposeOrFinalizeOperation: true);
        }

        public void SetHandleAsInvalid()
        {
            // Set closed state (low order bit of the _state field).
            Interlocked.Or(ref _state, StateBits.Closed);

            GC.SuppressFinalize(this);
        }

        protected abstract bool ReleaseHandle();

        public void DangerousAddRef(ref bool success)
        {
            // To prevent handle recycling security attacks we must enforce the
            // following invariant: we cannot successfully AddRef a handle on which
            // we've committed to the process of releasing.

            // We ensure this by never AddRef'ing a handle that is marked closed and
            // never marking a handle as closed while the ref count is non-zero. For
            // this to be thread safe we must perform inspection/updates of the two
            // values as a single atomic operation. We achieve this by storing them both
            // in a single aligned int and modifying the entire state via interlocked
            // compare exchange operations.

            // Additionally we have to deal with the problem of the Dispose operation.
            // We must assume that this operation is directly exposed to untrusted
            // callers and that malicious callers will try and use what is basically a
            // Release call to decrement the ref count to zero and free the handle while
            // it's still in use (the other way a handle recycling attack can be
            // mounted). We combat this by allowing only one Dispose to operate against
            // a given safe handle (which balances the creation operation given that
            // Dispose suppresses finalization). We record the fact that a Dispose has
            // been requested in the same state field as the ref count and closed state.

            // Might have to perform the following steps multiple times due to
            // interference from other AddRef's and Release's.
            int oldState, newState;
            do
            {
                // First step is to read the current handle state. We use this as a
                // basis to decide whether an AddRef is legal and, if so, to propose an
                // update predicated on the initial state (a conditional write).
                // Check for closed state.
                oldState = _state;
                ObjectDisposedException.ThrowIf((oldState & StateBits.Closed) != 0, this);

                // Not closed, let's propose an update (to the ref count, just add
                // StateBits.RefCountOne to the state to effectively add 1 to the ref count).
                // Continue doing this until the update succeeds (because nobody
                // modifies the state field between the read and write operations) or
                // the state moves to closed.
                newState = oldState + StateBits.RefCountOne;
            } while (Interlocked.CompareExchange(ref _state, newState, oldState) != oldState);

            // If we got here we managed to update the ref count while the state
            // remained non closed. So we're done.
            success = true;
        }

        // Used by internal callers to avoid declaring a bool to pass by ref
        internal void DangerousAddRef()
        {
            bool success = false;
            DangerousAddRef(ref success);
        }

        public void DangerousRelease() => InternalRelease(disposeOrFinalizeOperation: false);

        private void InternalRelease(bool disposeOrFinalizeOperation)
        {
            // See AddRef above for the design of the synchronization here. Basically we
            // will try to decrement the current ref count and, if that would take us to
            // zero refs, set the closed state on the handle as well.
            bool performRelease;

            // Might have to perform the following steps multiple times due to
            // interference from other AddRef's and Release's.
            int oldState, newState;
            do
            {
                // First step is to read the current handle state. We use this cached
                // value to predicate any modification we might decide to make to the
                // state).
                oldState = _state;

                // If this is a Dispose operation we have additional requirements (to
                // ensure that Dispose happens at most once as the comments in AddRef
                // detail). We must check that the dispose bit is not set in the old
                // state and, in the case of successful state update, leave the disposed
                // bit set. Silently do nothing if Dispose has already been called.
                if (disposeOrFinalizeOperation && ((oldState & StateBits.Disposed) != 0))
                {
                    return;
                }

                // We should never see a ref count of zero (that would imply we have
                // unbalanced AddRef and Releases). (We might see a closed state before
                // hitting zero though -- that can happen if SetHandleAsInvalid is
                // used).
                ObjectDisposedException.ThrowIf((oldState & StateBits.RefCount) == 0, this);

                // If we're proposing a decrement to zero and the handle is not closed
                // and we own the handle then we need to release the handle upon a
                // successful state update. If so we need to check whether the handle is
                // currently invalid by asking the SafeHandle subclass. We must do this before
                // transitioning the handle to closed, however, since setting the closed
                // state will cause IsInvalid to always return true.
                performRelease = ((oldState & (StateBits.RefCount | StateBits.Closed)) == StateBits.RefCountOne) &&
                                 _ownsHandle &&
                                 !IsInvalid;

                // Attempt the update to the new state, fail and retry if the initial
                // state has been modified in the meantime. Decrement the ref count by
                // subtracting StateBits.RefCountOne from the state then OR in the bits for
                // Dispose (if that's the reason for the Release) and closed (if the
                // initial ref count was 1).
                newState = oldState - StateBits.RefCountOne;
                if ((oldState & StateBits.RefCount) == StateBits.RefCountOne)
                {
                    newState |= StateBits.Closed;
                }
                if (disposeOrFinalizeOperation)
                {
                    newState |= StateBits.Disposed;
                }
            } while (Interlocked.CompareExchange(ref _state, newState, oldState) != oldState);

            // If we get here we successfully decremented the ref count. Additionally we
            // may have decremented it to zero and set the handle state as closed. In
            // this case (providing we own the handle) we will call the ReleaseHandle
            // method on the SafeHandle subclass.
            if (performRelease)
            {
                // Save last error from P/Invoke in case the implementation of ReleaseHandle
                // trashes it (important because this ReleaseHandle could occur implicitly
                // as part of unmarshaling another P/Invoke).
                int lastError = Marshal.GetLastPInvokeError();
                ReleaseHandle();
                Marshal.SetLastPInvokeError(lastError);
            }
        }
    }

    /// <summary>Defines an immutable value type that represents a floating type that has the same size as the native integer size.</summary>
    /// <remarks>It is meant to be used as an exchange type at the managed/unmanaged boundary to accurately represent in managed code unmanaged APIs that use a type alias for C or C++'s <c>float</c> on 32-bit platforms or <c>double</c> on 64-bit platforms, such as the CGFloat type in libraries provided by Apple.</remarks>
    [Intrinsic]
    [NonVersionable] // This only applies to field layout
    public readonly struct NFloat
    {
        private const NumberStyles DefaultNumberStyles = NumberStyles.Float | NumberStyles.AllowThousands;
    }

    /// <summary>This class contains methods that are mainly used to manage native memory.</summary>
    public static unsafe class NativeMemory
    {
        /// <summary>Allocates an aligned block of memory of the specified size and alignment, in bytes.</summary>
        public static void* AlignedAlloc(nuint byteCount, nuint alignment)
        {
            if (!BitOperations.IsPow2(alignment))
            {
                throw new ArgumentException("The alignment must be a power of two.");
            }

            // The allocation start sits right below the aligned block
            byte* allocation = (byte*)Marshal.AllocHGlobal((nint)(byteCount + alignment + (nuint)sizeof(void*)));
            if (allocation is null)
            {
                throw new OutOfMemoryException();
            }

            byte* aligned = (byte*)(((nuint)allocation + (nuint)sizeof(void*) + alignment - 1) & ~(alignment - 1));
            ((void**)aligned)[-1] = allocation;
            return aligned;
        }

        /// <summary>Frees an aligned block of memory.</summary>
        public static void AlignedFree(void* ptr)
        {
            if (ptr is not null)
            {
                Marshal.FreeHGlobal((nint)((void**)ptr)[-1]);
            }
        }
    }
}
