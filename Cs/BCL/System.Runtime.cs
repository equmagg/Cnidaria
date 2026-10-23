using Internal.Runtime;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Internal.Runtime
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ObjHeader
    {
        // Contents of the object header
        private IntPtr _objHeaderContents;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct DispatchMap
    {
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct DispatchMapEntry
        {
            internal ushort _usInterfaceIndex;
            internal ushort _usInterfaceMethodSlot;
            internal ushort _usImplMethodSlot;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct StaticDispatchMapEntry
        {
            // Do not put any other fields before this one. We need StaticDispatchMapEntry* be castable to DispatchMapEntry*.
            internal DispatchMapEntry _entry;
            internal ushort _usContextMapSource;
        }

        private ushort _standardEntryCount; // Implementations on the class
        private ushort _defaultEntryCount; // Default implementations
        private ushort _standardStaticEntryCount; // Implementations on the class (static virtuals)
        private ushort _defaultStaticEntryCount; // Default implementations (static virtuals)
        private DispatchMapEntry _dispatchMap; // at least one entry if any interfaces defined

        public uint NumStandardEntries
        {
            get
            {
                return _standardEntryCount;
            }
        }

        public uint NumDefaultEntries
        {
            get
            {
                return _defaultEntryCount;
            }
        }

        public uint NumStandardStaticEntries
        {
            get
            {
                return _standardStaticEntryCount;
            }
        }

        public uint NumDefaultStaticEntries
        {
            get
            {
                return _defaultStaticEntryCount;
            }
        }

        public int Size
        {
            get
            {
                return sizeof(ushort) + sizeof(ushort) + sizeof(ushort) + sizeof(ushort)
                    + sizeof(DispatchMapEntry) * ((int)_standardEntryCount + (int)_defaultEntryCount)
                    + sizeof(StaticDispatchMapEntry) * ((int)_standardStaticEntryCount + (int)_defaultStaticEntryCount);
            }
        }
    }

    // The head of the MethodTable every native code generator emits
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MethodTable
    {
#if TARGET_64BIT
        private const int POINTER_SIZE = 8;
        private const int PADDING = 1; // _numComponents is padded by one Int32 to make the first element pointer-aligned
#else
        private const int POINTER_SIZE = 4;
        private const int PADDING = 0;
#endif
        internal const int SZARRAY_BASE_SIZE = POINTER_SIZE + POINTER_SIZE + (1 + PADDING) * 4;

        [StructLayout(LayoutKind.Explicit)]
        private unsafe struct RelatedTypeUnion
        {
            // Kinds.CanonicalEEType
            [FieldOffset(0)]
            public MethodTable* _pBaseType;

            // Kinds.ArrayEEType
            [FieldOffset(0)]
            public MethodTable* _pRelatedParameterType;
        }

        /// <summary>
        /// Gets a value indicating whether the statically generated data structures use relative pointers.
        /// </summary>
        internal static bool SupportsRelativePointers
        {
            [Intrinsic]
            get
            {
                throw new NotImplementedException();
            }
        }

        [Intrinsic]
        internal static MethodTable* Of<T>()
        {
            throw new PlatformNotSupportedException();
        }

        // upper ushort is used for Flags
        // lower ushort is used for
        // - component size for strings and arrays,
        // - type arg count for generic type definitions MethodTables,
        // - otherwise holds ExtendedFlags bits
        private uint _uFlags;
        private uint _uBaseSize;
        private MethodTable* _relatedType;
        private ushort _usNumVtableSlots;
        private ushort _usNumInterfaces;
        private uint _uHashCode;

        // vtable follows

        internal bool HasComponentSize
        {
            get
            {
                // return (_uFlags & (uint)EETypeFlags.HasComponentSizeFlag) != 0;
                return (int)_uFlags < 0;
            }
        }

        internal ushort ComponentSize
        {
            get
            {
                return HasComponentSize ? (ushort)_uFlags : (ushort)0;
            }
#if TYPE_LOADER_IMPLEMENTATION
            set
            {
                _uFlags = (_uFlags & ~(uint)ushort.MaxValue) | value;
            }
#endif
        }

        internal ushort GenericParameterCount
        {
            get
            {
                return (ushort)_uBaseSize;
            }
        }

        internal uint Flags
        {
            get
            {
                return _uFlags;
            }
#if TYPE_LOADER_IMPLEMENTATION
            set
            {
                _uFlags = value;
            }
#endif
        }

        internal ushort ExtendedFlags
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return HasComponentSize ? (ushort)0 : (ushort)_uFlags;
            }
        }

        internal uint RawBaseSize
        {
            get
            {
                return _uBaseSize;
            }
#if TYPE_LOADER_IMPLEMENTATION
            set
            {
                _uBaseSize = value;
            }
#endif
        }

        internal uint BaseSize
        {
            get
            {
                return _uBaseSize;
            }
        }

        internal ushort NumVtableSlots
        {
            get
            {
                return _usNumVtableSlots;
            }
#if TYPE_LOADER_IMPLEMENTATION
            set
            {
                _usNumVtableSlots = value;
            }
#endif
        }

        internal ushort NumInterfaces
        {
            get
            {
                return _usNumInterfaces;
            }
#if TYPE_LOADER_IMPLEMENTATION
            set
            {
                _usNumInterfaces = value;
            }
#endif
        }

        internal uint HashCode
        {
            get
            {
                return _uHashCode;
            }
#if TYPE_LOADER_IMPLEMENTATION
            set
            {
                _uHashCode = value;
            }
#endif
        }

        private EETypeKind Kind
        {
            get
            {
                return (EETypeKind)(_uFlags & (uint)EETypeFlags.EETypeKindMask);
            }
        }

        // Mark or determine that a type is generic and one or more of it's type parameters is co- or
        // contra-variant. This only applies to interface and delegate types.
        internal bool HasGenericVariance
        {
            get
            {
                return (_uFlags & (uint)EETypeFlags.GenericVarianceFlag) != 0;
            }
        }

        internal bool IsFinalizable
        {
            get
            {
                return (_uFlags & (uint)EETypeFlags.HasFinalizerFlag) != 0;
            }
        }

        internal bool IsNullable
        {
            get
            {
                return ElementType == EETypeElementType.Nullable;
            }
        }

        internal bool IsDefType
        {
            get
            {
                EETypeKind kind = Kind;
                return kind == EETypeKind.CanonicalEEType || kind == EETypeKind.GenericTypeDefEEType;
            }
        }

        internal bool IsCanonical
        {
            get
            {
                return Kind == EETypeKind.CanonicalEEType;
            }
        }

        internal bool IsString
        {
            get
            {
                // String is currently the only non-array type with a non-zero component size.
                return ComponentSize == StringComponentSize.Value && IsCanonical;
            }
        }

        internal bool IsArray
        {
            get
            {
                EETypeElementType elementType = ElementType;
                return elementType == EETypeElementType.Array || elementType == EETypeElementType.SzArray;
            }
        }

        internal int ArrayRank
        {
            get
            {
                int boundsSize = (int)BaseSize - SZARRAY_BASE_SIZE;
                if (boundsSize > 0)
                {
                    // Multidim array case: Base size includes space for two Int32s
                    // (upper and lower bound) per each dimension of the array.
                    return (int)((uint)boundsSize / (uint)(2 * sizeof(int)));
                }
                return 1;
            }
        }

        // Returns rank of multi-dimensional array rank, 0 for sz arrays
        internal int MultiDimensionalArrayRank
        {
            get
            {
                int boundsSize = (int)this.BaseSize - SZARRAY_BASE_SIZE;
                // Multidim array case: Base size includes space for two Int32s
                // (upper and lower bound) per each dimension of the array.
                return (int)((uint)boundsSize / (uint)(2 * sizeof(int)));
            }
        }

        internal bool IsSzArray
        {
            get
            {
                return BaseSize == SZARRAY_BASE_SIZE;
            }
        }

        internal bool IsMultiDimensionalArray
        {
            get
            {
                return BaseSize > (uint)(3 * sizeof(IntPtr));
            }
        }

        internal bool IsGeneric
        {
            get
            {
                return (_uFlags & (uint)EETypeFlags.IsGenericFlag) != 0;
            }
        }

        internal bool IsGenericTypeDefinition
        {
            get
            {
                return Kind == EETypeKind.GenericTypeDefEEType;
            }
        }

        internal MethodTable* GenericDefinition
        {
            get
            {
                Debug.Assert(IsGeneric);

                uint offset = GetFieldOffset(EETypeField.ETF_GenericDefinition);

                if (IsDynamicType || !SupportsRelativePointers)
                    return GetField<Pointer<MethodTable>>(offset).Value;

                return GetField<RelativePointer<MethodTable>>(offset).Value;
            }
#if TYPE_LOADER_IMPLEMENTATION
            set
            {
                Debug.Assert(IsGeneric && IsDynamicType);
                GetField<IntPtr>(EETypeField.ETF_GenericDefinition) = (IntPtr)value;
            }
#endif
        }

#if TYPE_LOADER_IMPLEMENTATION
        internal static int GetGenericCompositionSize(int numArguments)
        {
            return numArguments * IntPtr.Size;
        }

        internal void SetGenericComposition(IntPtr data)
        {
            Debug.Assert(IsGeneric && IsDynamicType);
            GetField<IntPtr>(EETypeField.ETF_GenericComposition) = data;
        }
#endif

        internal uint GenericArity
        {
            get
            {
                return GenericDefinition->GenericParameterCount;
            }
        }

        internal MethodTableList GenericArguments
        {
            get
            {
                void* pField = (byte*)Unsafe.AsPointer(ref this) + GetFieldOffset(EETypeField.ETF_GenericComposition);
                uint arity = GenericArity;

                // If arity is 1, the field value is the component. For arity > 1, components are stored out-of-line
                // and are shared.
                if (IsDynamicType || !SupportsRelativePointers)
                {
                    // This is a full pointer [that points to a list of full pointers]
                    MethodTable* pListStart = arity == 1 ? (MethodTable*)pField : *(MethodTable**)pField;
                    return new MethodTableList(pListStart);
                }
                else
                {
                    // This is a relative pointer [that points to a list of relative pointers]
                    RelativePointer<MethodTable>* pListStart = arity == 1 ?
                        (RelativePointer<MethodTable>*)pField : (RelativePointer<MethodTable>*)((RelativePointer*)pField)->Value;
                    return new MethodTableList(pListStart);
                }
            }
        }

        internal GenericVariance* GenericVariance
        {
            get
            {
                if (!HasGenericVariance)
                    return null;

                if (IsGeneric)
                    return GenericDefinition->GenericVariance;

                uint offset = GetFieldOffset(EETypeField.ETF_GenericComposition);

                if (IsDynamicType || !SupportsRelativePointers)
                    return GetField<Pointer<GenericVariance>>(offset).Value;

                return GetField<RelativePointer<GenericVariance>>(offset).Value;
            }
        }

        internal bool IsPointer
        {
            get
            {
                return ElementType == EETypeElementType.Pointer;
            }
        }

        internal bool IsByRef
        {
            get
            {
                return ElementType == EETypeElementType.ByRef;
            }
        }

        internal bool IsInterface
        {
            get
            {
                return ElementType == EETypeElementType.Interface;
            }
        }

        internal bool IsByRefLike
        {
            get
            {
                return IsValueType && (_uFlags & (uint)EETypeFlagsEx.IsByRefLikeFlag) != 0;
            }
        }

        internal bool IsDynamicType
        {
            get
            {
                return (_uFlags & (uint)EETypeFlags.IsDynamicTypeFlag) != 0;
            }
        }

        internal bool IsParameterizedType
        {
            get
            {
                return Kind == EETypeKind.ParameterizedEEType;
            }
        }

        internal bool IsFunctionPointer
        {
            get
            {
                return Kind == EETypeKind.FunctionPointerEEType;
            }
        }

        // The parameterized type shape defines the particular form of parameterized type that
        // is being represented.
        // Currently, the meaning is a shape of 0 indicates that this is a Pointer,
        // shape of 1 indicates a ByRef, and >=SZARRAY_BASE_SIZE indicates that this is an array.
        // Two types are not equivalent if their shapes do not exactly match.
        internal uint ParameterizedTypeShape
        {
            get
            {
                return _uBaseSize;
            }
#if TYPE_LOADER_IMPLEMENTATION
            set
            {
                _uBaseSize = value;
            }
#endif
        }

        internal uint NumFunctionPointerParameters
        {
            get
            {
                return _uBaseSize & ~FunctionPointerFlags.FlagsMask;
            }
        }

        internal bool IsUnmanagedFunctionPointer
        {
            get
            {
                return (_uBaseSize & FunctionPointerFlags.IsUnmanaged) != 0;
            }
        }

        internal MethodTable* FunctionPointerReturnType
        {
            get
            {
                return _relatedType;
            }
#if TYPE_LOADER_IMPLEMENTATION
            set
            {
                Debug.Assert(IsDynamicType && IsFunctionPointer);
                _relatedType._pRelatedParameterType = value;
            }
#endif
        }

        internal bool RequiresAlign8
        {
            get
            {
                return (_uFlags & (uint)EETypeFlagsEx.RequiresAlign8Flag) != 0;
            }
        }

        internal bool IsIDynamicInterfaceCastable
        {
            get
            {
                return ((ExtendedFlags & (ushort)EETypeFlagsEx.IDynamicInterfaceCastableFlag) != 0);
            }
        }

        internal bool IsValueType
        {
            get
            {
                return ElementType < EETypeElementType.Class;
            }
        }

        // Warning! UNLIKE the similarly named Reflection api, this method also returns "true" for Enums.
        internal bool IsPrimitive
        {
            get
            {
                return ElementType < EETypeElementType.ValueType;
            }
        }

        internal bool HasSealedVTableEntries
        {
            get
            {
                return (_uFlags & (uint)EETypeFlags.HasSealedVTableEntriesFlag) != 0;
            }
        }

        internal bool ContainsGCPointers
        {
            get
            {
                return ((_uFlags & (uint)EETypeFlags.HasPointersFlag) != 0);
            }
#if TYPE_LOADER_IMPLEMENTATION
            set
            {
                if (value)
                {
                    _uFlags |= (uint)EETypeFlags.HasPointersFlag;
                }
                else
                {
                    _uFlags &= (uint)~EETypeFlags.HasPointersFlag;
                }
            }
#endif
        }

        internal bool IsTrackedReferenceWithFinalizer
        {
            get
            {
                return (ExtendedFlags & (ushort)EETypeFlagsEx.IsTrackedReferenceWithFinalizerFlag) != 0;
            }
        }

        internal uint ValueTypeFieldPadding
        {
            get
            {
                return (_uFlags & (uint)EETypeFlagsEx.ValueTypeFieldPaddingMask) >> ValueTypeFieldPaddingConsts.Shift;
            }
        }

        internal uint ValueTypeSize
        {
            get
            {
                // BaseSize returns the GC size including space for the sync block index field, the MethodTable* and
                // padding for GC heap alignment. Must subtract all of these to get the size used for locals, array
                // elements or fields of another type.
                return BaseSize - ((uint)sizeof(ObjHeader) + (uint)sizeof(MethodTable*) + ValueTypeFieldPadding);
            }
        }

        internal MethodTable* NullableType
        {
            get
            {
                return GenericArguments[0];
            }
        }

        internal MethodTable** InterfaceMap
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // interface info table starts after the vtable and has _usNumInterfaces entries
                return (MethodTable**)((byte*)Unsafe.AsPointer(ref this) + sizeof(MethodTable) + sizeof(void*) * _usNumVtableSlots);
            }
        }

        internal bool HasDispatchMap
        {
            get
            {
                return (_uFlags & (uint)EETypeFlags.HasDispatchMap) != 0;
            }
        }

        /// <summary>
        /// Gets the offset of the value embedded in a Nullable&lt;T&gt;.
        /// </summary>
        internal byte NullableValueOffset
        {
            get
            {
                int log2valueoffset = (int)(_uFlags & (ushort)EETypeFlagsEx.NullableValueOffsetMask) >> NullableValueOffsetConsts.Shift;
                return (byte)(1 << log2valueoffset);
            }
        }

        internal EETypeElementType ElementType
        {
            get
            {
                return (EETypeElementType)((_uFlags >> (byte)EETypeFlags.ElementTypeShift) &
                    ((uint)EETypeFlags.ElementTypeMask >> (byte)EETypeFlags.ElementTypeShift));
            }
#if TYPE_LOADER_IMPLEMENTATION
            set
            {
                _uFlags = (_uFlags & ~(uint)EETypeFlags.ElementTypeMask) | ((uint)value << (byte)EETypeFlags.ElementTypeShift);
            }
#endif
        }

        // This method is always called with a known constant and there's a lot of benefit in inlining it.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetFieldOffset(EETypeField eField)
        {
            // First part of MethodTable consists of the fixed portion followed by the vtable.
            uint cbOffset = (uint)(sizeof(MethodTable) + (IntPtr.Size * _usNumVtableSlots));

            // Followed by list of implemented interfaces
            cbOffset += (uint)(sizeof(MethodTable*) * NumInterfaces);

            uint relativeOrFullPointerOffset = (IsDynamicType || !SupportsRelativePointers ? (uint)IntPtr.Size : 4);

            // Followed by the type manager indirection cell.
            if (eField == EETypeField.ETF_TypeManagerIndirection)
            {
                return cbOffset;
            }
            cbOffset += relativeOrFullPointerOffset;

            // Followed by writable data.
            if (eField == EETypeField.ETF_WritableData)
            {
                return cbOffset;
            }
            cbOffset += relativeOrFullPointerOffset;

            // Followed by pointer to the dispatch map
            if (eField == EETypeField.ETF_DispatchMap)
            {
                return cbOffset;
            }
            if (HasDispatchMap)
                cbOffset += relativeOrFullPointerOffset;

            // Followed by the pointer to the finalizer method.
            if (eField == EETypeField.ETF_Finalizer)
            {
                return cbOffset;
            }
            if (IsFinalizable)
                cbOffset += relativeOrFullPointerOffset;

            // Followed by the pointer to the sealed virtual slots
            if (eField == EETypeField.ETF_SealedVirtualSlots)
                return cbOffset;

            // in the case of sealed vtable entries on static types, we have a UInt sized relative pointer
            if (HasSealedVTableEntries)
                cbOffset += relativeOrFullPointerOffset;

            if (eField == EETypeField.ETF_GenericDefinition)
            {
                return cbOffset;
            }
            if (IsGeneric)
            {
                cbOffset += relativeOrFullPointerOffset;
            }

            if (eField == EETypeField.ETF_GenericComposition)
            {
                return cbOffset;
            }
            if (IsGeneric || (IsGenericTypeDefinition && HasGenericVariance))
            {
                cbOffset += relativeOrFullPointerOffset;
            }

            if (eField == EETypeField.ETF_FunctionPointerParameters)
            {
                return cbOffset;
            }
            if (IsFunctionPointer)
            {
                cbOffset += NumFunctionPointerParameters * relativeOrFullPointerOffset;
            }

            if (eField == EETypeField.ETF_DynamicTemplateType)
            {
                return cbOffset;
            }
            if (IsDynamicType)
                cbOffset += (uint)IntPtr.Size;

            DynamicTypeFlags dynamicTypeFlags = 0;
            if (eField == EETypeField.ETF_DynamicTypeFlags)
            {
                return cbOffset;
            }
            if (IsDynamicType)
            {
                dynamicTypeFlags = (DynamicTypeFlags)GetField<nint>(cbOffset);
                cbOffset += (uint)IntPtr.Size;
            }

            if (eField == EETypeField.ETF_DynamicGcStatics)
            {
                return cbOffset;
            }
            if ((dynamicTypeFlags & DynamicTypeFlags.HasGCStatics) != 0)
                cbOffset += (uint)IntPtr.Size;

            if (eField == EETypeField.ETF_DynamicNonGcStatics)
            {
                return cbOffset;
            }
            if ((dynamicTypeFlags & DynamicTypeFlags.HasNonGCStatics) != 0)
                cbOffset += (uint)IntPtr.Size;

            if (eField == EETypeField.ETF_DynamicThreadStaticOffset)
            {
                return cbOffset;
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetField<T>(EETypeField eField)
        {
            return ref Unsafe.As<byte, T>(ref *((byte*)Unsafe.AsPointer(ref this) + GetFieldOffset(eField)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetField<T>(uint offset)
        {
            return ref Unsafe.As<byte, T>(ref *((byte*)Unsafe.AsPointer(ref this) + offset));
        }
    }

    // Wrapper around pointers
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct Pointer
    {
        private readonly IntPtr _value;

        public IntPtr Value => _value;
    }

    // Wrapper around pointers
    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct Pointer<T> where T : unmanaged
    {
        private readonly T* _value;

        public T* Value => _value;
    }

    // Wrapper around relative pointers
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct RelativePointer
    {
        private readonly int _value;

        public unsafe IntPtr Value => (IntPtr)((byte*)Unsafe.AsPointer(in _value) + _value);
    }

    // Wrapper around relative pointers
    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct RelativePointer<T> where T : unmanaged
    {
        private readonly int _value;

        public T* Value => (T*)((byte*)Unsafe.AsPointer(in _value) + _value);
    }

    // Abstracts a list of MethodTable pointers that could either be relative
    // pointers or full pointers. We store the IsRelative bit in the lowest
    // bit so this assumes the list is at least 2 byte aligned.
    internal readonly unsafe struct MethodTableList
    {
        private const int IsRelative = 1;

        private readonly void* _pFirst;

        public MethodTableList(MethodTable* pFirst)
        {
            _pFirst = pFirst;
        }

        public MethodTableList(RelativePointer<MethodTable>* pFirst)
        {
            _pFirst = (void*)((nint)pFirst | IsRelative);
        }

        public MethodTable* this[int index]
        {
            get
            {
                if (((nint)_pFirst & IsRelative) != 0)
                    return (((RelativePointer<MethodTable>*)((nint)_pFirst - IsRelative)) + index)->Value;

                return *((MethodTable**)_pFirst + index);
            }
#if TYPE_LOADER_IMPLEMENTATION
            set
            {
                Debug.Assert(((nint)_pFirst & IsRelative) == 0);
                *((MethodTable**)_pFirst + index) = value;
            }
#endif
        }
    }

    /// <summary>
    /// Represents the flags stored in the <c>_usFlags</c> field of a <c>System.Runtime.MethodTable</c>.
    /// </summary>
    [Flags]
    internal enum EETypeFlags : uint
    {
        /// <summary>
        /// There are four kinds of EETypes, defined in <c>Kinds</c>.
        /// </summary>
        EETypeKindMask = 0x00030000,

        /// <summary>
        /// Type has an associated dispatch map.
        /// </summary>
        HasDispatchMap = 0x00040000,

        /// <summary>
        /// This type was dynamically allocated at runtime.
        /// </summary>
        IsDynamicTypeFlag = 0x00080000,

        /// <summary>
        /// This MethodTable represents a type which requires finalization.
        /// </summary>
        HasFinalizerFlag = 0x00100000,

        /// <summary>
        /// This MethodTable has sealed vtable entries
        /// </summary>
        HasSealedVTableEntriesFlag = 0x00400000,

        /// <summary>
        /// This type is generic and one or more of its type parameters is co- or contra-variant. This
        /// only applies to interface and delegate types.
        /// </summary>
        GenericVarianceFlag = 0x00800000,

        /// <summary>
        /// This type contain GC pointers.
        /// </summary>
        HasPointersFlag = 0x01000000,

        /// <summary>
        /// This type is generic.
        /// </summary>
        IsGenericFlag = 0x02000000,

        /// <summary>
        /// We are storing a EETypeElementType in the upper bits for unboxing enums.
        /// </summary>
        ElementTypeMask = 0x7C000000,
        ElementTypeShift = 26,

        /// <summary>
        /// The _usComponentSize is a number (not holding FlagsEx).
        /// </summary>
        HasComponentSizeFlag = 0x80000000,
    };

    /// <summary>
    /// Represents the extra flags stored in the <c>_usComponentSize</c> field of a <c>System.Runtime.MethodTable</c>
    /// when <c>_usComponentSize</c> does not represent ComponentSize. (i.e. when the type is not an array, string or typedef)
    /// </summary>
    [Flags]
    internal enum EETypeFlagsEx : ushort
    {
        HasEagerFinalizerFlag = 0x0001,
        HasCriticalFinalizerFlag = 0x0002,
        IsTrackedReferenceWithFinalizerFlag = 0x0004,

        /// <summary>
        /// This type implements IDynamicInterfaceCastable to allow dynamic resolution of interface casts.
        /// </summary>
        IDynamicInterfaceCastableFlag = 0x0008,

        /// <summary>
        /// This MethodTable is for a Byref-like class (TypedReference, Span&lt;T&gt;,...)
        /// </summary>
        IsByRefLikeFlag = 0x0010,

        /// <summary>
        /// For valuetypes, stores the padding by which the type size is padded on the GC heap.
        /// </summary>
        ValueTypeFieldPaddingMask = 0x00E0,

        /// <summary>
        /// For nullable types, stores the log2 of offset of the value field.
        /// </summary>
        NullableValueOffsetMask = 0x0700,

        /// <summary>
        /// This type requires 8-byte alignment for its fields on certain platforms (ARM32, WASM)
        /// </summary>
        RequiresAlign8Flag = 0x1000
    }

    internal static class ValueTypeFieldPaddingConsts
    {
        public const int Shift = 5;
    }

    internal static class NullableValueOffsetConsts
    {
        public const int Shift = 8;
    }

    internal enum EETypeKind : uint
    {
        /// <summary>
        /// Represents a standard ECMA type
        /// </summary>
        CanonicalEEType = 0x00000000,

        /// <summary>
        /// Represents a function pointer
        /// </summary>
        FunctionPointerEEType = 0x00010000,

        /// <summary>
        /// Represents a parameterized type. For example a single dimensional array or pointer type
        /// </summary>
        ParameterizedEEType = 0x00020000,

        /// <summary>
        /// Represents an uninstantiated generic type definition
        /// </summary>
        GenericTypeDefEEType = 0x00030000,
    }

    /// <summary>
    /// Flags that are set for dynamically allocated types.
    /// </summary>
    [Flags]
    internal enum DynamicTypeFlags : int
    {
        /// <summary>
        /// This dynamically created type has a static constructor
        /// </summary>
        HasLazyCctor = 0x00000001,

        /// <summary>
        /// This dynamically created types has gc statics
        /// </summary>
        HasGCStatics = 0x00000002,

        /// <summary>
        /// This dynamically created types has non gc statics
        /// </summary>
        HasNonGCStatics = 0x00000004,

        /// <summary>
        /// This dynamically created types has thread statics
        /// </summary>
        HasThreadStatics = 0x00000008,
    }

    internal enum EETypeField
    {
        ETF_TypeManagerIndirection,
        ETF_WritableData,
        ETF_DispatchMap,
        ETF_Finalizer,
        ETF_SealedVirtualSlots,
        ETF_DynamicTemplateType,
        ETF_GenericDefinition,
        ETF_GenericComposition,
        ETF_FunctionPointerParameters,
        ETF_DynamicTypeFlags,
        ETF_DynamicGcStatics,
        ETF_DynamicNonGcStatics,
        ETF_DynamicThreadStaticOffset,
    }

    // Subset of the managed TypeFlags enum understood by the runtime.
    // This should match the values in the TypeFlags enum except for the special
    // entry that marks System.Array specifically.
    internal enum EETypeElementType
    {
        // Primitive
        Unknown = 0x00,
        Void = 0x01,
        Boolean = 0x02,
        Char = 0x03,
        SByte = 0x04,
        Byte = 0x05,
        Int16 = 0x06,
        UInt16 = 0x07,
        Int32 = 0x08,
        UInt32 = 0x09,
        Int64 = 0x0A,
        UInt64 = 0x0B,
        IntPtr = 0x0C,
        UIntPtr = 0x0D,
        Single = 0x0E,
        Double = 0x0F,

        ValueType = 0x10,
        // Enum = 0x11, // EETypes store enums as their underlying type
        Nullable = 0x12,
        // Unused 0x13,

        Class = 0x14,
        Interface = 0x15,

        SystemArray = 0x16, // System.Array type

        Array = 0x17,
        SzArray = 0x18,
        ByRef = 0x19,
        Pointer = 0x1A,
        FunctionPointer = 0x1B,
    }

    // Keep this synchronized with GenericVarianceType in rhbinder.h.
    internal enum GenericVariance : byte
    {
        NonVariant = 0,
        Covariant = 1,
        Contravariant = 2,
        ArrayCovariant = 0x20,
    }

    internal static class ParameterizedTypeShapeConstants
    {
        // NOTE: Parameterized type kind is stored in the BaseSize field of the MethodTable.
        // Array types use their actual base size. Pointer and ByRef types are never boxed,
        // so we can reuse the MethodTable BaseSize field to indicate the kind.
        // It's important that these values always stay lower than any valid value of a base
        // size for an actual array.
        public const int Pointer = 0;
        public const int ByRef = 1;
    }

    internal static class FunctionPointerFlags
    {
        public const uint IsUnmanaged = 0x80000000;
        public const uint FlagsMask = IsUnmanaged;
    }

    internal static class StringComponentSize
    {
        public const int Value = sizeof(char);
    }

    internal static class WritableData
    {
        public static int GetSize(int pointerSize) => pointerSize;
        public static int GetAlignment(int pointerSize) => pointerSize;
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

    // Exposed in Internal.CompilerServices only
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class RuntimeExportAttribute : Attribute
    {
        public string EntryPoint { get; }

        public RuntimeExportAttribute(string entry)
        {
            EntryPoint = entry;
        }
    }

    internal static class InternalCalls
    {
        private const string RuntimeLibrary = "*";

        //
        // internal calls for allocation
        //
        [RuntimeImport(RuntimeLibrary, "RhpNewFast")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern unsafe object RhpNewFast(MethodTable* pEEType);  // BEWARE: not for finalizable objects!

        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhBulkMoveWithWriteBarrier")]
        internal static extern unsafe void RhBulkMoveWithWriteBarrier(ref byte dmem, ref byte smem, nuint size);
    }

    internal static class RuntimeExports
    {
        [RuntimeExport("RhBoxAny")]
        public static unsafe object RhBoxAny(ref byte data, MethodTable* pEEType)
        {
            if (pEEType->IsValueType)
            {
                return RhBox(pEEType, ref data);
            }
            else
            {
                return Unsafe.As<byte, object>(ref data);
            }
        }

        [RuntimeExport("RhBox")]
        public static unsafe object RhBox(MethodTable* pEEType, ref byte data)
        {
            // A null can be passed for boxing of a null ref.
            _ = Unsafe.ReadUnaligned<byte>(ref data);

            ref byte dataAdjustedForNullable = ref data;

            // If we're boxing a Nullable<T> then either box the underlying T or return null (if the
            // nullable's value is empty).
            if (pEEType->IsNullable)
            {
                // The boolean which indicates whether the value is null comes first in the Nullable struct.
                if (data == 0)
                    return null;

                // Switch type we're going to box to the Nullable<T> target type and advance the data pointer
                // to the value embedded within the nullable.
                dataAdjustedForNullable = ref Unsafe.Add(ref data, pEEType->NullableValueOffset);
                pEEType = pEEType->NullableType;
            }

            object result;
#if FEATURE_64BIT_ALIGNMENT
            if (pEEType->RequiresAlign8)
            {
                result = InternalCalls.RhpNewFastMisalign(pEEType);
            }
            else
#endif // FEATURE_64BIT_ALIGNMENT
            {
                result = InternalCalls.RhpNewFast(pEEType);
            }

            // Copy the unboxed value type data into the new object.
            // Perform any write barriers necessary for embedded reference fields.
            if (pEEType->ContainsGCPointers)
            {
                InternalCalls.RhBulkMoveWithWriteBarrier(ref result.GetRawData(), ref dataAdjustedForNullable, pEEType->ValueTypeSize);
            }
            else
            {
                Unsafe.CopyBlock(ref result.GetRawData(), ref dataAdjustedForNullable, pEEType->ValueTypeSize);
            }

            return result;
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

        //
        // calls to runtime for allocation
        // These calls are needed in types which cannot use "new" to allocate and need to do it manually
        //
        // calls to runtime for allocation
        //
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport(RuntimeLibrary, "RhBoxAny")]
        internal static extern unsafe object RhBoxAny(ref byte pData, MethodTable* pEEType);

        // Those are not standard:

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

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetTypeCorElementType(IntPtr handle) => 0;

        // The register VM's importer sends MethodTable reads here, as it has no MethodTables
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetTypeElementType(IntPtr handle) => 0;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetTypeValueTypeSize(IntPtr handle) => 0;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static bool RhGetTypeContainsGCPointers(IntPtr handle) => false;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static IntPtr RhGetTypeNullableType(IntPtr handle) => 0;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetTypeNullableValueOffset(IntPtr handle) => 0;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetArrayLength(Array array, int dimension) => 0;

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetArrayLowerBound(Array array, int dimension) => 0;

        internal static int RhMultiplyArrayLengths(int total, int length, int lowerBound)
        {
            if (length < 0)
                throw new OverflowException();
            if ((long)lowerBound + length > (long)int.MaxValue + 1)
                throw new ArgumentOutOfRangeException(nameof(lowerBound));
            ulong product = (ulong)(uint)total * (uint)length;
            if (product > uint.MaxValue)
                throw new OutOfMemoryException();
            return unchecked((int)product);
        }

        internal static int RhValidateArrayLength(int total, bool dimensionTooLong)
        {
            if (total < 0 || dimensionTooLong)
                throw new OutOfMemoryException();
            return total;
        }

        // The object's identity hash, assigned on first request and stable for its lifetime
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhGetObjectHashCode(object obj) => 0;

        // The identity hash if one was already assigned, or 0
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static int RhTryGetObjectHashCode(object obj) => 0;
    }
}
