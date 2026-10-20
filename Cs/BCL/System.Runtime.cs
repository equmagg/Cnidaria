using System.Runtime.CompilerServices;

namespace Internal.Runtime
{

    // The head of the MethodTable every native code generator emits
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal unsafe struct MethodTable
    {
        private uint _uFlags;
        private uint _uBaseSize;
        private MethodTable* _relatedType;
        private ushort _usNumVtableSlots;
        private ushort _usNumInterfaces;
        private uint _uHashCode;

#if TARGET_64BIT
        internal const int SZARRAY_BASE_SIZE = 8 + 8 + 8;
#else
        internal const int SZARRAY_BASE_SIZE = 4 + 4 + 4;
#endif

        internal uint BaseSize => _uBaseSize;

        internal int ArrayRank
        {
            get
            {
                int boundsSize = (int)BaseSize - SZARRAY_BASE_SIZE;
                if (boundsSize > 0)
                {
                    // Multidim array case: Base size includes space for two Int32s
                    // (upper and lower bound) per each dimension of the array.
                    return boundsSize / (2 * sizeof(int));
                }
                return 1;
            }
        }
    }

    internal static class GCStaticRegionConstants
    {
        /// <summary>
        /// Flag set if the corresponding GCStatic entry has not yet been initialized and
        /// the corresponding MethodTable pointer has been changed into a instance pointer of
        /// that MethodTable.
        /// </summary>
        public const int Uninitialized = 0x1;

        /// <summary>
        /// Flag set if the next pointer loc points to GCStaticsPreInitDataNode.
        /// Otherise it is the next GCStatic entry.
        /// </summary>
        public const int HasPreInitializedData = 0x2;

        public const int Mask = Uninitialized | HasPreInitializedData;
    }

    internal static class ArrayTypesConstants
    {
        /// <summary>
        /// Maximum allowable size for array element types.
        /// </summary>
        public const int MaxSizeForValueClassInArray = 0xFFFF;
    }

    internal enum GC_ALLOC_FLAGS
    {
        GC_ALLOC_NO_FLAGS = 0,
        GC_ALLOC_FINALIZE = 1,
        GC_ALLOC_ALIGN8_BIAS = 4,
        GC_ALLOC_ALIGN8 = 8,
        GC_ALLOC_ZEROING_OPTIONAL = 16,
        GC_ALLOC_PINNED_OBJECT_HEAP = 64,
    }

    internal static class SpecialDispatchMapSlot
    {
        public const ushort Diamond = 0xFFFE;
        public const ushort Reabstraction = 0xFFFF;
    }

    internal static class DispatchMapCodePointerFlags
    {
        public const int RequiresInstantiatingThunkFlag = 2;
    }

    internal static class SpecialGVMInterfaceEntry
    {
        public const uint Diamond = 0xFFFFFFFF;
        public const uint Reabstraction = 0xFFFFFFFE;
    }

    internal static class StaticVirtualMethodContextSource
    {
        public const ushort None = 0;
        public const ushort ContextFromThisClass = 1;
        public const ushort ContextFromFirstInterface = 2;
    }
}
namespace System.Runtime
{
    // Exposed in Internal.CompilerServices only
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, Inherited = false)]
    internal sealed class RuntimeImportAttribute : Attribute
    {
        public string DllName { get; }
        public string EntryPoint { get; }

        public RuntimeImportAttribute(string entry)
        {
            EntryPoint = entry;
        }

        public RuntimeImportAttribute(string dllName, string entry)
        {
            EntryPoint = entry;
            DllName = dllName;
        }
    }

    internal static class RuntimeImports
    {
        internal const string RuntimeLibrary = "*";

        // Moves memory from smem to dmem. Size must be a positive value.
        // This copy uses an intrinsic to be safe for copying arbitrary bits of
        // heap memory
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhBulkMoveWithWriteBarrier")]
        internal static extern unsafe void RhBulkMoveWithWriteBarrier(ref byte dmem, ref byte smem, nuint size);

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static unsafe T[] RhAllocateNewArray<T>(int length, uint flags) => null!;

        // Those are not standard:
        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static IntPtr RhGetObjectTypeHandle(object obj) => default;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static string RhGetTypeName(IntPtr handle) => null!;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static string? RhGetTypeNamespace(IntPtr handle) => null;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static string? RhGetTypeFullName(IntPtr handle) => null;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static string RhGetTypeDisplayName(IntPtr handle) => null!;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static string RhGetTypeAssemblyName(IntPtr handle) => null!;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetTypeFlags(IntPtr handle) => 0;

        // The register VM has no MethodTables; its importer sends Array.Rank here
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetArrayRank(Array array) => 0;

        // The object's identity hash, assigned on first request and stable for its lifetime
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetObjectHashCode(object obj) => 0;

        // The identity hash if one was already assigned, or 0
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhTryGetObjectHashCode(object obj) => 0;
    }
}